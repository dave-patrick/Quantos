<#
.SYNOPSIS
  Stages a freshly built Quantos into the owner's copy of the app and starts it again.

.DESCRIPTION
  The owner tests Quantos from C:\Users\davem\Apps\Quantos\Quantos\application (their vault is
  C:\Users\davem\Quantos\Vault), so a build that isn't put there isn't being used. This script:
    1. optionally runs build/build.ps1,
    2. closes the running client and waits for its WebView2 browser processes to go,
    3. replaces the three programs in the application folder with the ones from
       publish\Quantos\application (renaming the old ones to *.old first, because a running
       .exe can be renamed but not overwritten),
    4. copies the updated documents across,
    5. starts Quantos.exe and checks the client log.

  If the new client fails with COMException 0x800700AA ("The requested resource is in use"),
  the WebView2 profile left behind by a hard-killed client is locked. The script then moves
  client-data\webview2 aside to webview2.bak-<time> and starts the client again on a fresh
  profile.

.EXAMPLE
  pwsh tools/dev/stage.ps1
  pwsh tools/dev/stage.ps1 -Build
#>
param(
    [switch]$Build,
    [string]$OwnerRoot = 'C:\Users\davem\Apps\Quantos\Quantos',
    [string]$Package = 'C:\repos\Quantos\publish\Quantos'
)
$ErrorActionPreference = 'Stop'
$app = Join-Path $OwnerRoot 'application'
$clientData = Join-Path $OwnerRoot 'client-data'
$logs = Join-Path $clientData 'logs'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# The client writes to client-<date>.log, so the file can change between two reads (at midnight).
# This remembers how much of each file has been read and gives back only what is new.
$seen = @{}
function New-LogText {
    $text = @()
    foreach ($log in Get-ChildItem (Join-Path $logs 'client-*.log') -ErrorAction SilentlyContinue) {
        $all = Get-Content $log.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -eq $all) { continue }
        $from = if ($seen.ContainsKey($log.FullName)) { $seen[$log.FullName] } else { 0 }
        if ($all.Length -gt $from) { $text += $all.Substring($from) }
        $seen[$log.FullName] = $all.Length
    }
    return ($text -join '')
}
if ($Build) {
    Step 'Building'
    # A staging copy carries the build mark, so it is told apart from the released version.
    pwsh -NoProfile -File 'C:\repos\Quantos\build\build.ps1' -Mark
    if ($LASTEXITCODE -ne 0) { throw "The build failed (exit $LASTEXITCODE)." }
}
if (-not (Test-Path (Join-Path $Package 'application\Quantos.exe'))) { throw "There is no built package at $Package. Run pwsh build/build.ps1 first." }

Step 'Stopping the client'
foreach ($process in Get-Process Quantos -ErrorAction SilentlyContinue) {
    try { $process.CloseMainWindow() | Out-Null } catch { }
}
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Process Quantos -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
foreach ($process in Get-Process Quantos -ErrorAction SilentlyContinue) { $process.Kill(); $process.WaitForExit(10000) | Out-Null }

# A hard-killed client leaves its WebView2 browser processes behind, and they keep the browser
# profile locked. Wait for them; end any that stay. The children (gpu, renderer, crashpad) do not
# carry the exe name but do carry the profile path, so both are matched.
function Get-QuantosWebViews {
    return @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*webview-exe-name=Quantos.exe*' -or $_.CommandLine -like '*Quantos\client-data\webview2*' -or -not $_.CommandLine })
}

function Stop-Quantos {
    foreach ($process in Get-Process Quantos -ErrorAction SilentlyContinue) {
        $process.Kill()
        try { $process.WaitForExit(15000) | Out-Null } catch { }
    }
}
$deadline = (Get-Date).AddSeconds(15)
do {
    $orphans = @(Get-QuantosWebViews)
    if ($orphans.Count -eq 0) { break }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)
foreach ($orphan in $orphans) { Stop-Process -Id $orphan.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
Write-Host "Quantos stopped ($($orphans.Count) leftover WebView2 processes ended)."
Step 'Replacing the program files'
foreach ($file in Get-ChildItem (Join-Path $Package 'application') -File) {
    $target = Join-Path $app $file.Name
    $old = "$target.old"
    # An old copy can still be running (the CLI is the MCP server), and then it cannot be
    # deleted. Leave it: the client clears *.old once it starts.
    if (Test-Path $old) {
        try { Remove-Item $old -Force -ErrorAction Stop }
        catch { $old = "$target.old-" + (Get-Date -Format 'yyyyMMdd-HHmmss') }
    }
    if (Test-Path $target) { Move-Item $target $old -Force }
    Copy-Item $file.FullName $target -Force
    Write-Host "  $($file.Name)"
}

Step 'Refreshing the documents'
foreach ($doc in 'USER-GUIDE.md', 'TROUBLESHOOTING.md', 'CLI-REFERENCE.md', 'PLUGINS.md', 'AGENT-ADAPTERS.md') {
    $from = Join-Path $Package $doc
    if (Test-Path $from) { Copy-Item $from (Join-Path $OwnerRoot $doc) -Force }
}

function Start-Client {
    # Remember what the log already says, so only this start's lines are looked at.
    New-LogText | Out-Null
    Start-Process (Join-Path $app 'Quantos.exe')
    Start-Sleep -Seconds 20
    return @(New-LogText -split "`r?`n")
}

Step 'Starting the client'
$lines = Start-Client
$lines | Write-Host
if (($lines -join "`n") -match 'Startup failed') {
    Step 'The browser profile is locked; starting on a fresh one'
    # The client keeps the profile folder open itself, so it has to be gone before the folder
    # can be moved aside.
    Stop-Quantos
    $profile = Join-Path $clientData 'webview2'
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $moved = $false
    for ($i = 0; $i -lt 30 -and -not $moved; $i++) {
        foreach ($orphan in @(Get-QuantosWebViews)) { & taskkill /PID $orphan.ProcessId /T /F 2>&1 | Out-Null }
        Start-Sleep -Seconds 1
        # The profile itself sits in webview2\EBWebView; moving that aside is enough, and it can
        # work when the folder above it cannot be moved.
        $inner = Join-Path $profile 'EBWebView'
        try {
            if (Test-Path $inner) { Rename-Item $inner "EBWebView.bak-$stamp" -ErrorAction Stop }
            elseif (Test-Path $profile) { Rename-Item $profile "webview2.bak-$stamp" -ErrorAction Stop }
            $moved = $true
        } catch { }
    }
    if (-not $moved) {
        # The client recovers on its own: it starts on a fresh profile when the old one is held.
        Write-Host 'The old profile could not be moved (a browser process is holding it); the client will start on a fresh one.'
    }
    else { Write-Host 'Started on a fresh browser profile.' }
    $lines = Start-Client
    $lines | Write-Host
}
if (($lines -join "`n") -match 'Startup failed') { throw 'The client did not start. Read the log above.' }
if (($lines -join "`n") -notmatch 'Page loaded') { Write-Host 'No "Page loaded" line yet; check the log in a moment.' }
Write-Host "`nDone." -ForegroundColor Green
