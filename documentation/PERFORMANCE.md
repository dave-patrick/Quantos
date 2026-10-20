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

**0.1.5.2 (2026-10-20):** a round of speed fixes — a note's frontmatter is parsed once instead of on
every lookup, saving no longer reads the note back off disk, the cache's queries are bounded, and the
editor stops rebuilding on every keystroke. Measured again on the same machine, but a busy one this
time (Quantos itself and two browsers were running), so the absolute times are far worse than the
table above and are **not** comparable with it. What is comparable is the same machine, back to back,
before and after the fix that went with 0.1.5.3:

| Operation | 0.1.5.2 | 0.1.5.3 |
|---|---|---|
| First open (index everything) | 39.2 s | 39.0 s |
| Restart with nothing changed | 975 ms | 790 ms |
| Full-text search | 29.3 ms | 30.9 ms |
| Backlinks for a note | 0.26 ms | 0.22 ms |
| Query across all notes (filter + sort) | 140 ms | 158 ms |
| Save (conflict check, history, atomic write, index) | 42.7 ms | 38.1 ms |
| Rename with link rewrites (27 referring notes, journaled) | 158 ms | 173 ms |
| Rebuild the cache from scratch | 27.8 s | 31.0 s |

The 0.1.5.3 column looks at every note in the vault instead of only the first few thousand, so the
query returns the notes it should — 1,667 of them, not 1,334 — for about the same time.

**Still to measure on the target laptop:** the same test on the KPMG laptop (antivirus scanning of
writes, and a slower disk, will raise save and first-open times), and a vault inside a synced folder.
