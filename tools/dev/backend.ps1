# Runs the dev backend against .dev\Vault in the foreground (tools/dev/start.ps1 starts it hidden).
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dev = Join-Path $root '.dev'
Set-Location $root
dotnet build src/Quantos.Cli --nologo *> "$dev\cli-build.log"
if ($LASTEXITCODE -ne 0) { throw 'The development CLI failed to build. See .dev/cli-build.log.' }
dotnet run --project src/Quantos.Backend --artifacts-path "$dev\artifacts" -- --vault "$dev\Vault" --client-data "$dev\client-data" --port 47821 --development-cli *> "$dev\backend.log"
