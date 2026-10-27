# Prints a one-time sign-in link for the dev backend, pointed at Vite (localhost:5173); -Backend for the built app.
param([switch]$Backend)
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$d = Get-Content (Join-Path $root '.dev\client-data\settings\backend.json') | ConvertFrom-Json
$cli = Join-Path $root 'src/Quantos.Cli/bin/Debug/net10.0/quantos-cli.exe'
if (-not (Test-Path -LiteralPath $cli)) { throw 'Restart the isolated Debug dev backend with tools/dev/backend.ps1 to build its paired CLI.' }
$link = & $cli open --print-url --client-data (Join-Path $root '.dev/client-data')
if ($LASTEXITCODE -ne 0) { throw 'Development sign-in failed. Restart the isolated Debug backend; discovery no longer grants browser sign-in.' }
if ($Backend) { $link } else { $link -replace '^http://127\.0\.0\.1:\d+/', 'http://localhost:5173/' }
