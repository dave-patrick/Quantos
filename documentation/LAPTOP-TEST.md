# Quantos laptop test (Stage 1, step 1)

This small program checks whether Quantos can run on a computer **without installing anything and without admin rights**. It changes nothing on your computer. Any test files it creates are deleted right away.

> **Before you start:** if this is a work laptop, check that your company allows running programs you downloaded yourself. Some IT policies forbid it even when it technically works.

## Steps

1. Download **Quantos-laptop-test** from the latest successful run on the repository's **Actions** tab (under "Artifacts"). Unzip it.
2. Put `Quantos.Probe.exe` in a normal folder, for example `Documents\Quantos-test` or `C:\Users\<you>\Quantos-test`.
3. Double-click `Quantos.Probe.exe`. **Don't** choose "Run as administrator".
   - If Windows shows **"Windows protected your PC"**, that is SmartScreen. Click **More info**, then **Run anyway**. Write down that you saw this.
   - If you get a message that the app is **blocked by your organization** (or nothing happens at all), that is the result. Take a screenshot.
4. Wait about 10 seconds while the checks run.
5. Click **Copy report** and paste it to Claude, or send the `quantos-laptop-test-….txt` file. It's saved next to the program, or on your Desktop.

## What the checks mean

| Check | Why Quantos needs it |
|---|---|
| This program was allowed to start | Quantos ships as unsigned `.exe` files. Application control (AppLocker/WDAC) can block them. |
| Running without admin rights | Quantos must work as a normal user. |
| Create files next to the app / default vault folder | The portable app and your notes need a folder you can write to. |
| Default vault isn't in a synced folder | OneDrive sync can lock or duplicate note files. |
| Local web server on 127.0.0.1 | The app's backend runs privately on your own computer. Some security tools block this. |
| SQLite | The rebuildable cache database. Its DLL is unpacked to your temp folder, which some policies block. |
| PowerShell engine | Needed for the planned terminal and automations. **Constrained Language** mode means company policy limits scripts. |
| WebView2 runtime | Displays Quantos's interface inside the Windows app. Built into Windows 10/11. |

**PASS** is good. **WARN** means Quantos works with a limitation. **FAIL** means that part of Quantos won't work on this computer as planned.
