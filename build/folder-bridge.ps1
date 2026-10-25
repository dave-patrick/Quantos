param([string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root "publish/folder-bridge/$Runtime"
$windowless=if($Runtime -like 'win-*'){'true'}else{'false'}
dotnet publish (Join-Path $root 'src/Quantos.FolderBridge') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none "-p:FolderBridgeWindowless=$windowless" -o $output --nologo
if($LASTEXITCODE -ne 0){throw 'Folder bridge build failed.'}
Write-Output "Folder bridge: $output"
