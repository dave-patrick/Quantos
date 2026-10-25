param(
    [Parameter(Mandatory)][string]$Folder,
    [Parameter(Mandatory)][string]$Workspace,
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string]$TokenFile,
    [Parameter(Mandatory)][string]$Binary,
    [string]$RuntimeDirectory=(Join-Path $env:USERPROFILE 'Apps\QuantosFolderBridge'),
    [switch]$Start
)
$ErrorActionPreference='Stop'
if($Workspace -notmatch '^[a-z0-9][a-z0-9_-]{0,63}$'){throw 'Use a lowercase workspace name.'}
$source=[IO.Path]::GetFullPath($Folder)
$runtime=[IO.Path]::GetFullPath($RuntimeDirectory)
if(-not(Test-Path -LiteralPath $source -PathType Container)){throw 'The source folder is unavailable.'}
if($runtime.StartsWith($source.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $runtime -eq $source){throw 'Keep bridge runtime outside the source folder.'}
if(-not(Test-Path -LiteralPath $Binary -PathType Leaf)){throw 'Build the folder bridge first.'}
if(-not(Test-Path -LiteralPath $TokenFile -PathType Leaf)){throw 'The private server-token file is missing.'}
$taskName="QuantosFolderBridge-$Workspace"
if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue){Stop-ScheduledTask -TaskName $taskName; Start-Sleep -Seconds 3}
New-Item -ItemType Directory -Force $runtime | Out-Null
& icacls $runtime /inheritance:r /grant:r "$($env:USERNAME):(OI)(CI)F" 'SYSTEM:(OI)(CI)F' | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not protect private bridge state.'}
$state=Join-Path $runtime $Workspace
New-Item -ItemType Directory -Force $state | Out-Null
$exe=Join-Path $state 'quantos-folder-bridge.exe'
Get-CimInstance Win32_Process -Filter "Name='quantos-folder-bridge.exe'" | Where-Object { $_.ExecutablePath -eq $exe } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
if(-not [string]::Equals([IO.Path]::GetFullPath($Binary),$exe,[StringComparison]::OrdinalIgnoreCase)){Copy-Item -LiteralPath $Binary -Destination $exe -Force}
# No password/key appears in the task command line; only its protected runtime file path.
foreach($value in @($source,$Workspace,$Server,$TokenFile,$state)){if($value.Contains('"')){throw 'Double quotes are not valid in bridge arguments.'}}
$config=Join-Path $state 'bridge-config.json'
@{Folder=$source;Workspace=$Workspace;Server=$Server;TokenFile=$TokenFile;State=$state;Executable=$exe} | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding UTF8
$runner=Join-Path $state 'run.ps1'
@'
$ErrorActionPreference='Stop'
$config=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bridge-config.json') -Raw | ConvertFrom-Json
& $config.Executable --folder $config.Folder --workspace $config.Workspace --server $config.Server --token-file $config.TokenFile --state $config.State
exit $LASTEXITCODE
'@ | Set-Content -LiteralPath $runner -Encoding UTF8
$arguments='--folder "{0}" --workspace "{1}" --server "{2}" --token-file "{3}" --state "{4}"' -f $source,$Workspace,$Server,$TokenFile,$state
# The Windows publish is a windowless executable. Track it directly so ending/restarting the task
# cannot leave a detached native worker holding the single-instance lock.
$action=New-ScheduledTaskAction -Execute $exe -Argument $arguments -WorkingDirectory $state
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$principal=New-ScheduledTaskPrincipal -UserId $identity.User.Value -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
if($Start){Start-ScheduledTask -TaskName $taskName}
Write-Output "Registered $taskName. Bridge state is protected outside the Google Drive folder."
