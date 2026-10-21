<#
.SYNOPSIS
  Stages a freshly built Quantos into the owner's copy of the app and starts it again.

.DESCRIPTION
  The owner runs Quantos from C:\Users\davem\Quantos\Quantos\application (their vault is
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
    [string]$OwnerRoot = 'C:\Users\davem\Quantos\Quantos',
    [string]$Package = 'C:\repos\Quantos\publish\Quantos'
)
$ErrorActionPreference = 'Stop'
$app = Join-Path $OwnerRoot 'application'
$clientData = Join-Path $OwnerRoot 'client-data'
$logs = Join-Path $clientData 'logs'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function ClientLogTail($lines) {
    $log = Get-ChildItem (Join-Path $logs 'client-*.log') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $log) { return @() }
    return @(Get-Content $log.FullName -Tail $lines)
}

if ($Build) {
    Step 'Building'
    pwsh -NoProfile -File 'C:\repos\Quantos\build\build.ps1'
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
# profile locked. Wait for them; end any that stay.
$deadline = (Get-Date).AddSeconds(15)
do {
    $orphans = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*webview-exe-name=Quantos.exe*' })
    if ($orphans.Count -eq 0) { break }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)
foreach ($orphan in $orphans) { Stop-Process -Id $orphan.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host "Quantos stopped ($($orphans.Count) leftover WebView2 processes ended)."

Step 'Replacing the program files'
foreach ($file in Get-ChildItem (Join-Path $Package 'application') -File) {
    $target = Join-Path $app $file.Name
    $old = "$target.old"
    if (Test-Path $old) { Remove-Item $old -Force }
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
    Start-Process (Join-Path $app 'Quantos.exe')
    Start-Sleep -Seconds 20
    return @(ClientLogTail 4)
}

Step 'Starting the client'
$lines = Start-Client
$lines | Write-Host
if (($lines -join "`n") -match '0x800700AA') {
    Step 'The browser profile is locked; starting on a fresh one'
    $profile = Join-Path $clientData 'webview2'
    if (Test-Path $profile) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        Rename-Item $profile "webview2.bak-$stamp"
        Write-Host "Moved the old profile to webview2.bak-$stamp."
    }
    $lines = Start-Client
    $lines | Write-Host
}
if (($lines -join "`n") -match 'Startup failed') { throw 'The client did not start. Read the log above.' }
if (($lines -join "`n") -notmatch 'Page loaded') { Write-Host 'No "Page loaded" line yet; check the log in a moment.' }
Write-Host "`nDone." -ForegroundColor Green
