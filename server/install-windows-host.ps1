param(
    [string]$Image = 'quantos-server:dev',
    [string]$RuntimeDirectory = (Join-Path $env:USERPROFILE 'Apps\QuantosServer'),
    [string]$ContainerName = 'quantos-server',
    [string]$VolumeName = 'quantos-data',
    [string]$TimeZone = 'America/Phoenix',
    [int]$Port = 47830,
    [int]$HttpsPort = 8445,
    # Also listen on this local-network address (for example 192.168.1.20), in addition to the loopback port Tailscale uses.
    [string]$LanAddress = '',
    [int]$LanPort = 47831,
    # For a server reached only on the local network, without Tailscale.
    [switch]$SkipTailscale
)
$ErrorActionPreference = 'Stop'
$keys = Join-Path $RuntimeDirectory 'keys'
New-Item -ItemType Directory -Force $keys | Out-Null
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
& docker volume create $VolumeName | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not create the persistent data volume.' }
$existing = & docker container ls -a --filter "name=^/$ContainerName`$" --format '{{.Names}}'
if ($existing -eq $ContainerName) {
    & docker stop --time 30 $ContainerName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop the previous server safely.' }
    & docker rename $ContainerName ($ContainerName + '-old-' + (Get-Date -Format yyyyMMdd-HHmmss))
    if ($LASTEXITCODE -ne 0) { throw 'Could not preserve the previous container.' }
}
$serverKey = Join-Path $keys 'server-token'
$mcpKey = Join-Path $keys 'mcp-token'
$publish = @('-p', "127.0.0.1:${Port}:8080")
if ($LanAddress) { $publish += @('-p', "${LanAddress}:${LanPort}:8080") }
& docker run -d --name $ContainerName --restart unless-stopped @publish `
    -v "${VolumeName}:/data" `
    --mount "type=bind,source=$serverKey,target=/run/secrets/server-token,readonly" `
    --mount "type=bind,source=$mcpKey,target=/run/secrets/mcp-token,readonly" `
    -e 'QUANTOS_SERVER_TOKEN_FILE=/run/secrets/server-token' `
    -e 'QUANTOS_MCP_TOKEN_FILE=/run/secrets/mcp-token' -e "TZ=$TimeZone" $Image | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not start Quantos Server. The previous container and vault have been kept.' }
$healthy = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try {
        $health = Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 2
        if ($health.ok) { $healthy = $true; break }
    } catch { }
    Start-Sleep -Seconds 1
}
if (-not $healthy) { throw 'Quantos Server did not become healthy. Inspect docker logs quantos-server.' }
if (-not $SkipTailscale) {
    & tailscale serve --bg "--https=$HttpsPort" "http://127.0.0.1:$Port"
    if ($LASTEXITCODE -ne 0) { throw 'The server is healthy locally, but Tailscale HTTPS could not be configured.' }
}
Write-Output "Quantos Server is healthy. Vault volume: $VolumeName; keys are protected in the runtime keys folder."
if ($LanAddress) {
    # The firewall is not changed for you. Allow the port only from your local network, then use http://${LanAddress}:${LanPort}/mcp for MCP.
    Write-Output "Listening on the local network at http://${LanAddress}:${LanPort} (plain HTTP: the MCP key is not encrypted on the wire). To allow it through Windows Firewall from your local subnet only, run as administrator:"
    Write-Output "  New-NetFirewallRule -DisplayName 'Quantos Server (LAN)' -Direction Inbound -Protocol TCP -LocalPort $LanPort -RemoteAddress LocalSubnet -Action Allow"
}
