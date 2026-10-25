# Android app and shared server settings

MODE: Operate. The owner uses the existing Quantos workspace on a phone or tablet, locally and offline,
with optional shared-vault sync. The user explicitly requires the same branding and features.

SYSTEM: Extend the established application, preserving `frontend/src/styles/tokens.css`, real
wordmark/icon, Source Sans 3 body, Chakra Petch display and IBM Plex Mono editor/data typography.
No separate visual world or image comp replaces the existing Quantos interface.

STORY: Open a local vault, write or complete work, and optionally connect it to the shared server.
The server MCP's write permission is controlled in Settings › MCP and confirmed by the actual server.

FIRST VIEWPORT: The same neutral charcoal workspace, 8px gutters and blue accent. Compact phones use
a 112px two-row header, a single centre workspace and overlay sidebars. Tablets retain the wider
60px header and persistent sidebars when space permits. Controls have 48dp targets. The native host
keeps content inside system, cutout and keyboard insets. Split dividers accept touch and keyboard.

INTERACTION: Android Back closes the active overlay before navigating workspace history or leaving.
Local autosave and drafts precede leaving. Sync never blocks editing; concurrent edits preserve
both copies and are resolved in Shared vault settings.

QUALITY BAR: Preserve the actual desktop product's assets and functionality. Verify the installed
Android app on phone and tablet viewports, including enlarged text. Verify desktop settings in the
existing browser harness. No browser capture is evidence of native runtime verification.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict,
DESIGN.md, and every shipping raster carrying its provenance

EXTENSION VERIFICATION (2026-10-25): Compared the Android host, compact-device CSS, shared-vault/MCP settings and editor resize handling with the incumbent `frontend/src/styles/tokens.css`, `frontend/public/brand` assets and bundled fonts. The extension retains the charcoal/blue palette, real Quantos icon/wordmark and existing typography; its documented platform adaptations are 48dp targets, compact navigation, system/IME insets and keeping the focused caret visible when the keyboard opens. No new global visual tokens or brand assets were introduced. Inspected native evidence in `.impeccable/review/`: `phone.png`, `phone-keyboard.png`, `phone-large-text.png`, `phone-mcp.png`, `phone-mcp-large-text.png`, `tablet.png` and `tablet-mcp.png`; `desktop-mcp.png` is desktop browser evidence. The finish reviewer reported the caret fix resolved and disposition ship; runtime PDF, sync and MCP checks remain recorded by the implementing agent. Existing marketing-oriented `PRODUCT.md`/`DESIGN.md` drift is reported, not repaired; those files, `.impeccable/design.json`, incumbent tokens, fonts and brand assets are preserved.
