<#
.SYNOPSIS
  Starts everything needed to run and check Quantos in a browser, for development and for agents.
  Work data (a test vault copied from the example vault, client data, a browser profile, logs) goes
  in .dev\ at the repo root, which git ignores. Safe to run again: running parts are left alone.

.EXAMPLE
  pwsh tools/dev/start.ps1          # backend on 47821, Vite on 5173, headless Edge on 9333
  pwsh tools/dev/start.ps1 -Fresh   # start from a fresh copy of the example vault
#>
param([switch]$Fresh, [switch]$NoEdge, [switch]$FakeAi)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dev = Join-Path $root '.dev'
if ($Fresh -and (Test-Path $dev)) {
  & (Join-Path $PSScriptRoot 'stop.ps1')
  Remove-Item -Recurse -Force (Join-Path $dev 'Vault'), (Join-Path $dev 'client-data') -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force $dev | Out-Null
$vault = Join-Path $dev 'Vault'
if (-not (Test-Path $vault)) { Copy-Item -Recurse (Join-Path $root 'examples\Example Vault') $vault; Write-Host "Test vault: $vault" }

function Up($url) { try { Invoke-WebRequest $url -TimeoutSec 2 -ErrorAction Stop | Out-Null; $true } catch { [bool]$_.Exception.Response } }

# 1. Backend (dotnet run, so it always reflects the current source; restart it after backend changes).
if (-not (Up 'http://127.0.0.1:47821/api/ping')) {
  Write-Host 'Starting the backend…'
  Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile', '-File', (Join-Path $PSScriptRoot 'backend.ps1')
  foreach ($i in 1..90) { if (Up 'http://127.0.0.1:47821/api/ping') { break }; Start-Sleep 2 }
}
# 2. Vite dev server (hot reload; proxies /api to the backend).
if (-not (Up 'http://localhost:5173/')) {
  Write-Host 'Starting Vite…'
  Start-Process pwsh -WindowStyle Hidden -WorkingDirectory (Join-Path $root 'frontend') -ArgumentList '-NoProfile', '-Command', "npx vite --strictPort *> '$dev\vite.log'"
  foreach ($i in 1..30) { if (Up 'http://localhost:5173/') { break }; Start-Sleep 1 }
}
# 3. Optional fake AI provider (OpenAI-style, on 127.0.0.1:11555) for trying the AI features without an account.
if ($FakeAi -and -not (Up 'http://127.0.0.1:11555/models')) { Start-Process node -WindowStyle Hidden -ArgumentList (Join-Path $PSScriptRoot 'fakeai.mjs') }
# 4. Headless Edge with the DevTools protocol on 9333, driven by tools/dev/edge.mjs.
if (-not $NoEdge -and -not (Up 'http://127.0.0.1:9333/json/version')) {
  $edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
  Start-Process $edge -ArgumentList '--headless=new', '--remote-debugging-port=9333', "--user-data-dir=$dev\edge-profile", '--window-size=1440,900', 'about:blank'
  Start-Sleep 2
}
# Open the app signed in.
$link = & (Join-Path $PSScriptRoot 'signin.ps1')
if (-not $NoEdge) { node (Join-Path $PSScriptRoot 'edge.mjs') 9333 nav $link | Out-Null }
Write-Host "Ready. App: http://localhost:5173 (sign in with: pwsh tools/dev/signin.ps1). Drive it with: node tools/dev/edge.mjs 9333 <eval|shot|nav|key|type|click|drag> …"
