# Measured performance

BUILD-PLAN section 13 says not to claim any speed until it has been measured on a realistic vault.
These are measurements, not targets.

**How measured:** `tests/Quantos.Tests/PerformanceTests.cs`, run with
`$env:QUANTOS_PERF="1"; dotnet test tests/Quantos.Tests -c Release --filter PerformanceTests`.
It writes a vault of 5,000 notes (3–12 paragraphs each, three links, a task line, frontmatter with
tags and a status), then times the operations below. Machine: Windows 11, .NET 10.0, local SSD,
2026-10-15.

| Operation | 5,000 notes |
|---|---|
| First open of an existing vault (index everything) | 9.7 s |
| Restart with nothing changed | 0.17 s |
| Full-text search | 12 ms per query |
| Backlinks for a note | 0.07 ms |
| Query across all notes (filter + sort) | 27 ms |
| Save (conflict check, history, atomic write, index) | 21 ms |
| Rename with link rewrites (27 referring notes, journaled) | 67 ms |
| Rebuild the cache from scratch | 7.7 s |

Changes made after the first measurement: the start-up scan no longer copies never-before-seen files
into history (the file on disk is the only version), and index writes during scans and rebuilds share
one transaction. First open went from 30.7 s to 9.7 s and a rebuild from 15.3 s to 7.7 s.

**Still to measure on the target laptop:** the same test on the KPMG laptop (antivirus scanning of
writes, and a slower disk, will raise save and first-open times), and a vault inside a synced folder.
