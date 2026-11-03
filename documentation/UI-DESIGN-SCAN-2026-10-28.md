# Interface review and fixes — 28 October 2026

## Shared interface fixes

Private build 2026.28.10.21 retains semantic version 0.2.4. No public release is requested.
All 391 automated tests passed during the change. The final touch/label adjustments also passed
typecheck and the 15 browser assertions with no axe violations. Windows build 21 passed ZIP
validation and self-check (zero warnings); the owner’s normal test copy restarted at 14:24:24
with HTTP 200 and all three executable hashes matching. The preceding build 20 used the retained
WebView profile fallback; final build 21 restarted normally. The private ARM64 build 21 compiled
without warnings/errors, passed signature verification, and installed over the debug app without
clearing data. Physical confirmation of the final controls is recorded below.

- Keyboard focus reveals Add to My Day and its focus ring.
- Status text uses the readable muted colour at 12px. The default dark faint-text token is also
  brighter, covering secondary labels, hints and task placeholders on the base surface.
- Task filters and view controls wrap within their pane; long task metadata wraps within its row.
- Default sidebar destinations are grouped into Everyday, Planning, and Explore and tools.
  Advanced groups start collapsed and open when their destination is selected elsewhere.
  Existing custom ordering and hidden items are retained. Customise sidebar has a visible label.
- Appearance begins with a theme picker and interface size. Theme previews, colours and fonts
  remain available under Theme colours and fonts. Writing controls stay near the top and
  settings choices wrap in narrow panes.

`frontend/tests/uiRefinements.html` mounts the real components against synthetic data. Its 15
browser assertions cover focus visibility, long metadata, a 450px task pane, a 354px Appearance
pane, advanced disclosure, all 12 theme previews, sidebar navigation and saved custom order,
and dark status contrast. The final fixture has no axe violations. This is browser evidence;
it does not certify native Windows or Android interactions.

## Windows scan — source and startup logs

Native automation could not discover apps: the Windows Computer Use helper timed out on
list_apps twice, again after resetting its session, and through the alternate app inventory.
No interactive Windows accessibility, DPI, maximize/restore or screenshot certification is claimed.
The installed client log reviewed during the scan records HTTP 200 and a 2,858ms startup at
12:52:36. That dated startup record does not prove the new build's behaviour.

Additional source-supported findings, not changed in this shared-interface patch:

1. **P2 — Caption accessibility.** `src/Quantos.Client/CaptionButton.cs:12` draws custom glyphs on
   a Control with no accessible name, button role or default action, and disables TabStop.
   The host removes the native caption. Expose named button semantics and an invoke action;
   Maximize must change its name to Restore with the state.
2. **P2 — Restored bounds.** `src/Quantos.Client/MainForm.cs:73` accepts saved bounds if any part intersects a work
   area. After a monitor change, the title bar may remain offscreen. Clamp saved bounds to a
   current work area while keeping the title bar reachable.
3. **P2 — Scratchpad placement.** `src/Quantos.Client/PopupForm.cs:44` places a popup one quarter down the work area
   without clamping its bottom. A 520px scratchpad in a 600px work area can extend below the
   screen. Fit and clamp the popup when opening or changing monitors.

Positive source checks: PerMonitorV2, DPI-scaled resize borders, trusted-main-page palette
validation, and a resizable script review dialog with selectable text and Cancel as the default.

## Android scan — physical phone and source

The physical Galaxy S26 Ultra (SM-S948U) connected through wireless ADB after the initial
emulator attempts. Its native WebView is 411 × 834 CSS pixels at devicePixelRatio 1.75.
The first installed private build was 2026.28.10.20.

Native checks completed on that phone:

- Notes folders open and Android Back returns to All folders in the existing workspace.
- Focusing note search opens the actual Samsung IME. The viewport shrinks to 479px; the search
  input/caret remains visible and app navigation stays above the keyboard. Back dismisses it.
- Tasks Options fits and wraps within the screen without horizontal overflow.
- Appearance disclosure opens by native tap and reflows without overflow; custom appearance is preserved.
- System bars and application navigation remain unobscured.

The scan found three Android issues corrected in the final source/build set:

1. Theme colours and fonts measured only 41.875px tall. ThemeEditor summary now has a 48px minimum.
2. Header Capture measured 43 × 48px and had no accessible name when its text was hidden.
   PhoneShell now provides Quick capture as its accessible label and a nonshrinking 48px minimum width.
3. About read version 0.2.3 from the embedded backend while Android App info read 0.2.4/code 204.
   Android backend metadata now matches the established release version 0.2.4.

Final build 2026.28.10.21 is installed and physically confirmed: About shows 0.2.4/build 21,
Capture has AX button semantics with the name Quick capture and a 48 × 48px target, and the theme
disclosure is 48px high, opens/closes and has no overflow. The app is restored to Notes > All folders
in the existing workspace with no open dialog. Note/workspace counts match the pre-update snapshot;
no note/task content was entered or edited. The owner confirmed that the theme change during the scan was intentional and asked to keep the
current theme. No restoration or recovery preference writes were made; the current selection is preserved.
The existing workspace has no tasks, so native long-metadata verification there was unavailable;
the synthetic browser fixture covers that case. TalkBack, tablet, landscape and enlarged system
text remain untested. No broad native health score is assigned.

Native screenshots are in ignored .dev/ui-native-scan: device-start.png,
device-appearance-closed.png, device-appearance-open.png, device-notes-keyboard.png and
device-tasks-options.png and device21-notes-final.png. Positive source checks also include max(system bars, IME) insets,
WebView text zoom following Android font scale, native Back dispatch, 48px header targets and
52px More rows.

Initial emulator limitations: QuantosPhone failed during Android startup with disappearing
framework services and SystemUI DeadSystemException. A bounded cold boot of QuantosReleaseQA
also never exposed package/activity services. Neither failure is an observed Quantos defect.
Both owned emulators were stopped without wiping data or changing device settings.
