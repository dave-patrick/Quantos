# Runs the dev backend against .dev\Vault in the foreground (tools/dev/start.ps1 starts it hidden).
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dev = Join-Path $root '.dev'
Set-Location $root
dotnet run --project src/Quantos.Backend --artifacts-path "$dev\artifacts" -- --vault "$dev\Vault" --client-data "$dev\client-data" --port 47821 *> "$dev\backend.log"
