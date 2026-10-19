# Prints a one-time sign-in link for the dev backend, pointed at Vite (localhost:5173); -Backend for the built app.
param([switch]$Backend)
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$d = Get-Content (Join-Path $root '.dev\client-data\settings\backend.json') | ConvertFrom-Json
$r = Invoke-RestMethod -Method Post -Uri "$($d.url)api/auth/code" -Headers @{ Authorization = "Bearer $($d.token)"; 'X-Quantos-Client' = 'windows' } -ContentType 'application/json' -Body '{"kind":"browser"}'
if ($Backend) { $r.url } else { $r.url -replace '^http://127\.0\.0\.1:\d+/', 'http://localhost:5173/' }
