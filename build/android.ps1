param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('android-arm64', 'android-x64')][string]$Runtime = 'android-arm64',
    [string]$AndroidSdk = (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
    [switch]$SkipFrontend
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if (-not $SkipFrontend) {
    Push-Location frontend
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'The frontend build failed.' }
    } finally { Pop-Location }
}
dotnet build android/Quantos.Android.csproj -c $Configuration "-p:RuntimeIdentifier=$Runtime" "-p:AndroidSdkDirectory=$AndroidSdk" --nologo
if ($LASTEXITCODE -ne 0) { throw 'The Android build failed.' }
$source = Join-Path $root "android\bin\$Configuration\net10.0-android\$Runtime\com.atomic22.quantos-Signed.apk"
if (-not (Test-Path -LiteralPath $source)) { throw 'The build did not produce a signed APK.' }
$output = Join-Path $root 'publish\android'
New-Item -ItemType Directory -Force $output | Out-Null
$target = Join-Path $output "Quantos-0.2.0-$Runtime-$Configuration.apk"
Copy-Item -LiteralPath $source -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($target + '.sha256', "$hash  $([IO.Path]::GetFileName($target))`n")
Write-Output "Android APK: $target"
Write-Output "SHA-256: $hash"
