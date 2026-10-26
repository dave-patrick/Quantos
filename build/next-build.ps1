param()
$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot 'build-id.txt'
$previous = (Get-Content -LiteralPath $path -Raw).Trim()
if ($previous -notmatch '^\d{4}\.\d{2}\.\d{2}\.(\d+)$') { throw 'Invalid previous build ID.' }
$revision = [int]$Matches[1] + 1
$today = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'America/Phoenix')
$next = $today.ToString('yyyy.dd.MM') + '.' + $revision
[IO.File]::WriteAllText($path, "$next`n")
Write-Output $next
