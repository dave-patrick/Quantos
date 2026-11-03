<#
.SYNOPSIS
  Runs Quantos Server directly on Windows, without Docker.

.DESCRIPTION
  Publishes the server from this checkout (or uses a folder you built already), creates the two private keys,
  and starts it hidden. It serves the shared vault's sync routes and the Streamable HTTP MCP endpoint at /mcp,
  exactly as the Docker install does. By default it also registers a scheduled task that starts it at sign-in.

  The server time zone is this computer's Windows time zone (the Docker install uses -TimeZone instead).

.PARAMETER Listen
  Where to listen. The default is this computer only. Examples: http://127.0.0.1:47830 (this computer),
  http://192.168.1.20:47830 (your local network address; plain HTTP, so the keys and notes are not encrypted
  on the wire), http://0.0.0.0:47830 (every network adapter). Separate several with semicolons.

.PARAMETER Binary
  A folder that already holds a published Quantos.Server.exe. Without it the script runs dotnet publish
  (the .NET SDK is needed once, to build).

.EXAMPLE
  pwsh server/install-windows-native.ps1 -Start
  pwsh server/install-windows-native.ps1 -Listen http://192.168.1.20:47830 -Start
  pwsh server/install-windows-native.ps1 -Uninstall
#>
param(
    [string]$RuntimeDirectory = (Join-Path $env:USERPROFILE 'Apps\QuantosServerNative'),
    [string]$Listen = 'http://127.0.0.1:47830',
    [string]$Binary = '',
    [switch]$Start,
    [switch]$NoTask,
    # Also publish the loopback port over Tailscale HTTPS (needs Tailscale).
    [int]$TailscaleHttpsPort = 0,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$taskName = 'Quantos Server (native)'
$app = Join-Path $RuntimeDirectory 'app'
$data = Join-Path $RuntimeDirectory 'data'
$keys = Join-Path $RuntimeDirectory 'keys'
$exe = Join-Path $app 'Quantos.Server.exe'

function Stop-Server {
    Get-Process -Name 'Quantos.Server' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe } | ForEach-Object { $_.Kill(); $_.WaitForExit(30000) | Out-Null }
}

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false }
    Stop-Server
    Write-Output "Stopped and removed the scheduled task. Your vault and keys are untouched in $RuntimeDirectory."
    return
}

# 1. The program.
if ($Binary) {
    if (-not (Test-Path -LiteralPath (Join-Path $Binary 'Quantos.Server.exe'))) { throw "No Quantos.Server.exe in $Binary." }
    Stop-Server
    New-Item -ItemType Directory -Force $app | Out-Null
    Copy-Item -Path (Join-Path $Binary '*') -Destination $app -Recurse -Force
} else {
    $source = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Quantos.Server'
    if (-not (Test-Path -LiteralPath $source)) { throw 'Run this from a Quantos checkout, or pass -Binary.' }
    $staging = Join-Path $RuntimeDirectory 'publish-staging'
    if (Test-Path -LiteralPath $staging) { Remove-Item -Recurse -Force $staging }
    & dotnet publish $source -c Release -r win-x64 --self-contained -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    Stop-Server
    New-Item -ItemType Directory -Force $app | Out-Null
    Copy-Item -Path (Join-Path $staging '*') -Destination $app -Recurse -Force
    Remove-Item -Recurse -Force $staging
}

# 2. The keys: one for the Quantos apps (sync and administration), one for MCP agents.
New-Item -ItemType Directory -Force $keys, $data | Out-Null
foreach ($name in @('server-token', 'mcp-token')) {
    $file = Join-Path $keys $name
    if (-not (Test-Path -LiteralPath $file)) {
        $bytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        [IO.File]::WriteAllText($file, [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant())
    }
}
& icacls $keys /inheritance:r /grant:r "$($env:USERNAME):(OI)(CI)F" 'SYSTEM:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not protect the server keys.' }

# 3. A small launcher that sets the server's settings and runs it.
$launcher = Join-Path $RuntimeDirectory 'run-server.ps1'
@"
`$env:QUANTOS_SERVER_DATA = '$data'
`$env:QUANTOS_SERVER_TOKEN_FILE = '$(Join-Path $keys 'server-token')'
`$env:QUANTOS_MCP_TOKEN_FILE = '$(Join-Path $keys 'mcp-token')'
`$env:ASPNETCORE_URLS = '$Listen'
& '$exe'
"@ | Set-Content -LiteralPath $launcher -Encoding UTF8

# 4. Start at sign-in.
if (-not $NoTask) {
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$launcher`""
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Description 'Quantos Server without Docker' -Force | Out-Null
}

# 5. Start now.
if ($Start) {
    Stop-Server
    Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass', '-File', $launcher) -WindowStyle Hidden
    $first = ($Listen -split ';')[0]
    $probe = [Uri]($first -replace '//(\+|\*|0\.0\.0\.0)', '//127.0.0.1')
    $healthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try { if ((Invoke-RestMethod "$($probe.GetLeftPart('Authority'))/health" -TimeoutSec 2).ok) { $healthy = $true; break } } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $healthy) { throw "Quantos Server did not become healthy at $($probe.GetLeftPart('Authority')). Run $launcher in a console to see why." }
    if ($TailscaleHttpsPort -gt 0) {
        & tailscale serve --bg "--https=$TailscaleHttpsPort" "http://127.0.0.1:$($probe.Port)"
        if ($LASTEXITCODE -ne 0) { throw 'The server is healthy, but Tailscale HTTPS could not be configured.' }
    }
    Write-Output "Quantos Server is running at $Listen (health OK)."
} else {
    Write-Output "Installed. Start it with: powershell -File `"$launcher`"  (or sign out and in, or re-run with -Start)."
}
Write-Output "MCP endpoint: <address>/mcp with the key in $(Join-Path $keys 'mcp-token')  (Authorization: Bearer <key>)."
Write-Output "App key (Settings › Shared vault in the apps): $(Join-Path $keys 'server-token'). Do not share either file."
if ($Listen -notmatch '127\.0\.0\.1|localhost') {
    Write-Output 'Listening beyond this computer over plain HTTP. Allow the port through Windows Firewall for your local subnet only, e.g. (administrator):'
    $port = ([Uri](($Listen -split ';')[0] -replace '//(\+|\*|0\.0\.0\.0)', '//127.0.0.1')).Port
    Write-Output "  New-NetFirewallRule -DisplayName 'Quantos Server (native)' -Direction Inbound -Protocol TCP -LocalPort $port -RemoteAddress LocalSubnet -Action Allow"
}
