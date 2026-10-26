param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('android-arm64', 'android-x64')][string]$Runtime = 'android-arm64',
    [string]$AndroidSdk = (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
    [switch]$SkipFrontend,
    [string]$SigningKeyStore,
    [string]$SigningKeyAlias = 'quantos-release',
    [string]$SigningPasswordFile,
    [string]$SigningKeyPasswordFile
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$signing = @()
if ($Configuration -eq 'Release') {
    if (-not $SigningKeyStore -or -not $SigningPasswordFile -or -not $SigningKeyPasswordFile -or
        -not (Test-Path -LiteralPath $SigningKeyStore -PathType Leaf) -or
        -not (Test-Path -LiteralPath $SigningPasswordFile -PathType Leaf) -or
        -not (Test-Path -LiteralPath $SigningKeyPasswordFile -PathType Leaf)) {
        throw 'Release requires a private signing keystore and separate store/key password files. Debug signing is only for private test builds.'
    }
    if ([IO.Path]::GetFullPath($SigningPasswordFile) -eq [IO.Path]::GetFullPath($SigningKeyPasswordFile)) {
        throw 'Use separate password file paths: apksigner consumes one line per password from each file.'
    }
    $signing = @('-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$([IO.Path]::GetFullPath($SigningKeyStore))",
        "-p:AndroidSigningKeyAlias=$SigningKeyAlias", "-p:AndroidSigningStorePass=file:$([IO.Path]::GetFullPath($SigningPasswordFile))",
        "-p:AndroidSigningKeyPass=file:$([IO.Path]::GetFullPath($SigningKeyPasswordFile))")
    # A failed signer can leave an aligned but unsigned *-Signed.apk which incremental MSBuild
    # considers current. Force the final signing step and verify it before exporting anything.
    $oldSigned = Join-Path $root "android/bin/Release/net10.0-android/$Runtime/com.atomic22.quantos-Signed.apk"
    if (Test-Path -LiteralPath $oldSigned) { Remove-Item -LiteralPath $oldSigned -Force }
}
if (-not $SkipFrontend) {
    Push-Location frontend
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'The frontend build failed.' }
    } finally { Pop-Location }
}
dotnet build android/Quantos.Android.csproj -c $Configuration "-p:RuntimeIdentifier=$Runtime" "-p:AndroidSdkDirectory=$AndroidSdk" @signing --nologo
if ($LASTEXITCODE -ne 0) { throw 'The Android build failed.' }
$source = Join-Path $root "android\bin\$Configuration\net10.0-android\$Runtime\com.atomic22.quantos-Signed.apk"
if (-not (Test-Path -LiteralPath $source)) { throw 'The build did not produce a signed APK.' }
if ($Configuration -eq 'Release') {
    $buildTools = Get-ChildItem (Join-Path $AndroidSdk 'build-tools') -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    & (Join-Path $buildTools.FullName 'apksigner.bat') verify --verbose $source
    if ($LASTEXITCODE -ne 0) { throw 'Release signature verification failed; no public APK was exported.' }
    $badging = & (Join-Path $buildTools.FullName 'aapt2.exe') dump badging $source
    if ($LASTEXITCODE -ne 0 -or $badging -match 'application-debuggable|testOnly') {
        throw 'Release APK metadata verification failed.'
    }
}
$output = Join-Path $root 'publish\android'
New-Item -ItemType Directory -Force $output | Out-Null
$project = [xml](Get-Content (Join-Path $root 'android/Quantos.Android.csproj') -Raw)
$version = $project.Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
$buildId = (Get-Content (Join-Path $root 'build/build-id.txt') -Raw).Trim()
if ($buildId -notmatch '^\d{4}\.\d{2}\.\d{2}\.[1-9]\d*$') { throw 'Build ID must use YYYY.DD.MM.revision.' }
$target = Join-Path $output "Quantos-$version-build-$buildId-$Runtime-$Configuration.apk"
Copy-Item -LiteralPath $source -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($target + '.sha256', "$hash  $([IO.Path]::GetFileName($target))`n")
Write-Output "Android APK: $target"
Write-Output "SHA-256: $hash"
