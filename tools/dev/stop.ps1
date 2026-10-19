# Stops the dev backend, Vite, the fake AI provider and the headless Edge started by start.ps1.
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Get-CimInstance Win32_Process | Where-Object {
  ($_.Name -match 'Quantos.Backend|dotnet' -and $_.CommandLine -match '47821') -or
  ($_.Name -eq 'node.exe' -and $_.CommandLine -match 'vite|fakeai') -or
  ($_.Name -eq 'msedge.exe' -and $_.CommandLine -match 'remote-debugging-port=9333')
} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host 'Stopped.'
