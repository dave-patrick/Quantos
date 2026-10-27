<#
.SYNOPSIS
  Builds the no-install Quantos package: Quantos\application\ (three self-contained single-file
  executables), the user guide, the example vault, and a zip of it all.

.PARAMETER Mark
  Puts a build mark (the commit the package was built from) next to the version in the app's
  wordmark. Use it for a staging copy that is being tried out; a released build has no mark.

.EXAMPLE
  pwsh build/build.ps1            # full build with tests
  pwsh build/build.ps1 -SkipTests
  pwsh build/build.ps1 -Mark      # a staging copy, marked with the commit
#>
param(
    [switch]$SkipTests,
    [switch]$Mark,
    [string]$Runtime = 'win-x64',
    [string]$Version = '0.2.3'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$out = Join-Path $root 'publish'
$pkg = Join-Path $out 'Quantos'
$app = Join-Path $pkg 'application'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# The build mark is read by the frontend build; an unmarked build leaves it empty.
if ($Mark) {
    $commit = (& git -C $root rev-parse --short HEAD 2>$null | Select-Object -First 1)
    if (-not $commit) { throw 'Could not read the commit for the build mark.' }
    # Say so when the working tree has changes the commit does not have.
    $dirty = (& git -C $root status --porcelain 2>$null | Select-Object -First 1)
    $env:QUANTOS_BUILD = "build $commit$(if ($dirty) { '-dirty' })"
    Write-Host "Build mark: $env:QUANTOS_BUILD"
}
else { $env:QUANTOS_BUILD = '' }

Step 'Frontend'
Push-Location frontend
if (-not (Test-Path node_modules)) { npm ci --no-audit --no-fund | Out-Host }
npx vite build | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Frontend build failed (exit $LASTEXITCODE)" }
if (-not $SkipTests) { npx vitest run | Out-Host; if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed' } }
Pop-Location
if (-not (Test-Path 'src/Quantos.Backend/wwwroot/index.html')) { throw 'Frontend build produced no index.html' }

if (-not $SkipTests) {
    Step 'Tests'
    dotnet test tests/Quantos.Tests -c Release --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    if ($IsWindows) {
        dotnet test tests/Quantos.Client.Tests -c Release --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Native client tests failed' }
    }
}

Step 'Publish'
if (Test-Path $pkg) { Remove-Item -LiteralPath $pkg -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null
$common = @('-c', 'Release', '-r', $Runtime, '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=false', '-p:DebugType=none', "-p:Version=$Version", '--nologo', '-o', $app)
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
Copy-Item documentation/PLUGINS.md $pkg
Copy-Item documentation/AGENT-ADAPTERS.md $pkg
Copy-Item documentation/ANDROID.md $pkg
Copy-Item documentation/SERVER.md $pkg
Copy-Item examples/agents/quantos_tools.py $pkg
Copy-Item LICENSE $pkg -ErrorAction SilentlyContinue
Copy-Item -Recurse 'examples/Example Vault' (Join-Path $pkg 'Example Vault')
Set-Content (Join-Path $pkg 'Start Quantos.cmd') -Encoding ascii -Value "@echo off`r`nstart `"`" `"%~dp0application\Quantos.exe`""
Set-Content (Join-Path $pkg 'Open in browser.cmd') -Encoding ascii -Value "@echo off`r`n`"%~dp0application\Quantos.Backend.exe`" --open"
Set-Content (Join-Path $pkg 'Self-check.cmd') -Encoding ascii -Value "@echo off`r`n`"%~dp0application\Quantos.Backend.exe`" --selfcheck`r`npause"

Step 'Zip'
$buildId = (Get-Content (Join-Path $root 'build/build-id.txt') -Raw).Trim()
if ($buildId -notmatch '^\d{4}\.\d{2}\.\d{2}\.[1-9]\d*$') { throw 'Build ID must use YYYY.DD.MM.revision.' }
$zip = Join-Path $out "Quantos-$Version-build-$buildId-$Runtime.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $pkg -DestinationPath $zip -CompressionLevel Optimal

Step 'Check the package'
# The updater only installs a package laid out as Quantos\application\..., and it copies single files
# from that folder, so the three programs have to be whole single-file executables there. A package
# built any other way is refused on the owner's machine ("This isn't a Quantos package").
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $required = 'Quantos/application/Quantos.exe', 'Quantos/application/Quantos.Backend.exe',
                'Quantos/application/quantos-cli.exe', 'Quantos/USER-GUIDE.md', 'Quantos/ANDROID.md', 'Quantos/SERVER.md',
                'Quantos/Start Quantos.cmd', 'Quantos/Example Vault/automations.json'
    foreach ($name in $required) {
        if ($names -notcontains $name) { throw "The package is missing $name, so the updater could not install it." }
    }
}
finally { $archive.Dispose() }

$size = '{0:N1} MB' -f ((Get-Item $zip).Length / 1MB)
Write-Host "`nDone: $zip ($size)" -ForegroundColor Green
Get-ChildItem $app | Select-Object Name, @{ n = 'MB'; e = { '{0:N1}' -f ($_.Length / 1MB) } } | Format-Table | Out-Host
