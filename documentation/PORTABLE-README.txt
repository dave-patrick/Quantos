QUANTOS (early Stage 1 build, for testing only)
Published by Atomic 22 Industries

This build is for testing the foundation. Keep real notes elsewhere until the daily-use checkpoint.

START
  Double-click application\Quantos.exe
  Quantos creates its notes folder ("vault") at C:\Users\<you>\Quantos\Vault
  and its rebuildable cache in the client-data folder next to "application".

BROWSER INSTEAD OF THE WINDOWS APP
  Double-click application\Quantos.Backend.exe. Your browser opens Quantos.
  Keep the black window open while you use it. Close it to stop.

CLOSING
  Closing the Quantos window only minimizes it. Use the Exit button
  (top right) or Ctrl+Q to save everything and shut down safely.

BACKUP (manual, Release 1.0)
  1. Exit Quantos.
  2. Copy the whole Vault folder somewhere safe.
  You don't need to back up client-data\cache. It can be rebuilt.

OPTIONS (advanced)
  Quantos.Backend.exe --vault "D:\MyVault"      use a different vault folder
  Quantos.Backend.exe --port 48000              use a different local port
