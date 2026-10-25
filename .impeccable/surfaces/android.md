# Android app and shared server settings

MODE: Operate. The owner uses the existing Quantos workspace on a phone or tablet, locally and offline,
with optional shared-vault sync. The user explicitly requires the same branding and features.

SYSTEM: Extend the established application, preserving `frontend/src/styles/tokens.css`, real
wordmark/icon, Source Sans 3 body, Chakra Petch display and IBM Plex Mono editor/data typography.
No separate visual world or image comp replaces the existing Quantos interface.

STORY: Open a local vault, write or complete work, and optionally connect it to the shared server.
The server MCP's write permission is controlled in Settings › MCP and confirmed by the actual server.

FIRST VIEWPORT (phone revision approved 2026-10-25): The owner finds the desktop workspace too
complicated on a phone but appropriate on a tablet. Phones now use a 56dp app bar, one visible page,
and a 68dp Material-style navigation bar for Notes, Tasks, Today and More. Capture stays in the app
bar; Notes offers one New note FAB and a readable recent-note list. Advanced views, saved views,
workspace switching and settings remain reachable through More. Details replaces the phone page
temporarily and has an explicit close action. Note properties move into Details, and secondary note
actions move into the overflow menu. Tablets retain the 60px workspace header, split panes and
persistent sidebars. Brand assets/tokens are inherited, with no new visual world or image comp.
Controls have 48dp targets; native system/IME insets and caret visibility remain required.

PHONE NAVIGATION EXTENSION (2026-10-25): Settings › Phone navigation lets the owner pick one to
four destinations and order them with explicit left/right controls. More stays last and cannot be
removed. Changes apply immediately and persist on this device. The owner's chosen defaults are
Daily notes, Tasks, Capture and Today; Capture opens quick capture while retaining the current page.
Use the incumbent settings rows, icons, palette and 48dp controls. Tablets keep their workspace.

INTERACTION: Android Back closes the active overlay before navigating workspace history or leaving.
Local autosave and drafts precede leaving. Sync never blocks editing; concurrent edits preserve
both copies and are resolved in Shared vault settings.
Closing the last right-hand panel hides the sidebar and preserves its configuration for reopening.
Phone navigation must retain the existing editor while More or Details is shown; open tabs and
tablet split layouts remain available rather than being discarded during compact navigation.

QUALITY BAR: Preserve the actual desktop product's assets and functionality. Verify the installed
Android app on phone and tablet viewports, including enlarged text. Verify desktop settings in the
existing browser harness. No browser capture is evidence of native runtime verification.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict,
DESIGN.md, and every shipping raster carrying its provenance

EXTENSION VERIFICATION (2026-10-25): Compared the Android host, compact-device CSS, shared-vault/MCP settings and editor resize handling with the incumbent `frontend/src/styles/tokens.css`, `frontend/public/brand` assets and bundled fonts. The extension retains the charcoal/blue palette, real Quantos icon/wordmark and existing typography; its documented platform adaptations are 48dp targets, compact navigation, system/IME insets and keeping the focused caret visible when the keyboard opens. No new global visual tokens or brand assets were introduced. Inspected native evidence in `.impeccable/review/`: `phone.png`, `phone-keyboard.png`, `phone-large-text.png`, `phone-mcp.png`, `phone-mcp-large-text.png`, `tablet.png` and `tablet-mcp.png`; `desktop-mcp.png` is desktop browser evidence. The finish reviewer reported the caret fix resolved and disposition ship; runtime PDF, sync and MCP checks remain recorded by the implementing agent. Existing marketing-oriented `PRODUCT.md`/`DESIGN.md` drift is reported, not repaired; those files, `.impeccable/design.json`, incumbent tokens, fonts and brand assets are preserved.

PHONE REVISION VERIFICATION (2026-10-25): Compared PhoneShell.svelte, PhonePanel.svelte, components/views/PhoneNotes.svelte and android.css with the incumbent tokens, bundled fonts and Quantos wordmark; the phone extension retains the charcoal/blue palette, Source Sans 3 body, Chakra Petch headings and IBM Plex Mono editor/data typography without new tokens, brand assets or shipping rasters. Inspected native phone Notes, keyboard/caret, full-text search and touch Capture evidence plus the expanded tablet workspace in .impeccable/review/phone-revision/. The native x64 checks recorded by the implementing agent cover 360 CSS-pixel phone navigation, Details close, Android Back from More with the editor retained, 48dp task-label targets, Options, 1.3 text scale, a body-only search match and touch-first Capture copy; the 1280 CSS-pixel tablet retains split panes and sidebars, including hiding and reopening the last right-hand panel with its configuration preserved. The initial full finish review covered the remaining revision; follow-up scoring passed the requested search and Capture fixes, reported no remaining findings and disposition ship. This records that extension evidence only, not a new certification of every feature. Existing marketing-oriented PRODUCT.md/DESIGN.md drift remains reported and preserved; global DESIGN.md, PRODUCT.md, design.json, tokens, fonts and brand assets are unchanged by this documentation pass.

NAVIGATION CUSTOMIZATION VERIFICATION (2026-10-25): Compared PhoneNavigationSettings.svelte, PhoneShell.svelte, phoneNavigation.ts, prefs.svelte.ts and SettingsView.svelte with the incumbent tokens and icons. Settings offers one to four unique known shortcuts, explicit ordering, device-local persistence and Reset to Daily notes, Tasks, Capture and Today; More remains last, and Capture opens quick capture over the retained page. The extension preserves the charcoal/blue palette, Source Sans 3 body, Chakra Petch headings and IBM Plex Mono editor/data typography, with no new global tokens, brand assets or shipping rasters. Native evidence reviewed by the implementing agent in .impeccable/review/phone-navigation/ comprises phone-large-text.png (360 CSS pixels, 1.3 text scale), phone-owner.png (411 CSS pixels on the owner's phone), phone-custom.png, phone-capture.png and tablet.png. The full finish review covered defaults, customization, Capture and the retained tablet workspace; follow-up scoring resolved its sole spacing finding with 8px reorder gaps and 48px targets and returned disposition ship. This verifies the navigation extension only; notes availability and shared-vault sync are outside this review. Existing marketing-oriented PRODUCT.md/DESIGN.md drift remains reported and preserved; those files, design.json, tokens, fonts and assets are unchanged by this documentation pass.
