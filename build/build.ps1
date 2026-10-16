<#
.SYNOPSIS
  Builds the no-install Quantos package: Quantos\application\ (three self-contained single-file
  executables), the user guide, the example vault, and a zip of it all.

.EXAMPLE
  pwsh build/build.ps1            # full build with tests
  pwsh build/build.ps1 -SkipTests
#>
param(
    [switch]$SkipTests,
    [string]$Runtime = 'win-x64',
    [string]$Version = '0.1.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$out = Join-Path $root 'publish'
$pkg = Join-Path $out 'Quantos'
$app = Join-Path $pkg 'application'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

Step 'Frontend'
Push-Location frontend
if (-not (Test-Path node_modules)) { npm ci --no-audit --no-fund | Out-Host }
npx vite build | Out-Host
Pop-Location
if (-not (Test-Path 'src/Quantos.Backend/wwwroot/index.html')) { throw 'Frontend build produced no index.html' }

if (-not $SkipTests) {
    Step 'Tests'
    dotnet test tests/Quantos.Tests -c Release --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}

Step 'Publish'
if (Test-Path $pkg) { Remove-Item -LiteralPath $pkg -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null
$common = @('-c', 'Release', '-r', $Runtime, '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none', "-p:Version=$Version", '--nologo', '-o', $app)
foreach ($project in 'src/Quantos.Backend', 'src/Quantos.Client', 'src/Quantos.Cli') {
    dotnet publish $project @common | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
# Single-file builds need nothing beside the executables.
Get-ChildItem -LiteralPath $app -File | Where-Object { $_.Extension -ne '.exe' } | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

Step 'Documents and example vault'
Copy-Item documentation/USER-GUIDE.md $pkg
Copy-Item documentation/TROUBLESHOOTING.md $pkg
Copy-Item documentation/CLI-REFERENCE.md $pkg
Copy-Item LICENSE $pkg -ErrorAction SilentlyContinue
Copy-Item -Recurse 'examples/Example Vault' (Join-Path $pkg 'Example Vault')
Set-Content (Join-Path $pkg 'Start Quantos.cmd') -Encoding ascii -Value "@echo off`r`nstart `"`" `"%~dp0application\Quantos.exe`""
Set-Content (Join-Path $pkg 'Open in browser.cmd') -Encoding ascii -Value "@echo off`r`n`"%~dp0application\Quantos.Backend.exe`" --open"
Set-Content (Join-Path $pkg 'Self-check.cmd') -Encoding ascii -Value "@echo off`r`n`"%~dp0application\Quantos.Backend.exe`" --selfcheck`r`npause"

Step 'Zip'
$zip = Join-Path $out "Quantos-$Version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $pkg -DestinationPath $zip -CompressionLevel Optimal
$size = '{0:N1} MB' -f ((Get-Item $zip).Length / 1MB)
Write-Host "`nDone: $zip ($size)" -ForegroundColor Green
Get-ChildItem $app | Select-Object Name, @{ n = 'MB'; e = { '{0:N1}' -f ($_.Length / 1MB) } } | Format-Table | Out-Host
