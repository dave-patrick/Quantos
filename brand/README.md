# Quantos brand

The wordmark is the app icon used as the letter **Q**, followed by `uantos`.

**Locked:** Bruno Ace SC, caps, outlined, **flat treatment**. See
[`BRAND-SPEC.txt`](BRAND-SPEC.txt) for the ratios and the rules that hold it together.

## Use it

`quantos-wordmark.svg` is the deliverable. It is self-contained — the lettering is
outlined from the font binary, so there is no webfont or font dependency of any
kind. It fills from `currentColor`, so it inherits whatever colour you set.

```html
<svg viewBox="-4 105.9 609.36 128" style="color:#F2F6FC"><!-- contents --></svg>
```

`quantos-wordmark-glow.svg` is the same wordmark with the icon's original 3D
treatment kept. Identical lettering, gap and centring — only the treatment
differs. It is not the shipped asset.

`quantos-wordmark-final.html` previews both, on light and dark, at five sizes.

## The two ratios

| | ratio | absolute |
|---|---|---|
| Q : cap height | 1.993 : 1 | 120 / 60.2 |
| gap : Q | 0.150 : 1 | 18 / 120 |

Four things that are easy to get wrong, and are corrected here:

- **`getBBox` excludes stroke.** It reports geometry only and never sees a
  filter. The arcs carry `stroke-width: 128`, so it understated the mark by
  64 units a side — sizing off it made the Q render at ~131u instead of 120u.
  All extents here come from pixel scans of the rendered icon instead.
- **The icon's ink is inset inside its artboard** — it sits 21 units below the
  centre of its 1020-unit box. Spacing measured box-to-box is wrong, and
  centring on the box centre puts the Q low.
- **Faces don't share an x-height.** Gruppo's is 0.374 em, Anta's 0.512. The Q
  has to move vertically per face to stay centred on the letterform's body band.
- **Every face gives its first letter a different side bearing** — Gruppo's `u`
  adds 11 units, Turret Road's 5.4. The gap is measured ink-edge to glyph-ink,
  so the bearing is paid off or the faces drift apart.

Flat's frame is its ink, so its box is exactly `MARK` — no hidden margin.

## Rebuilding

```sh
node fetch-fonts.js     # fonts/ is gitignored; third-party binaries
node render.js          # -> preview.template.html
node build-preview.js   # -> preview.html          (six-option comparison)
node build-final.js     # -> quantos-wordmark.svg  (the deliverable)
                        #    quantos-wordmark-glow.svg  (alternative)
                        #    BRAND-SPEC.txt
                        #    quantos-wordmark-final.html
node verify-geom.js     # structure of the comparison page
node verify-final.js    # re-derives the ratios from the exported SVGs themselves
```

`set.js` holds the tunables — `TREATMENT`, `SIZE_DELTA`, `GAP`, `MARK`, and
the per-face sizes. Everything downstream is computed from real font metrics
read out of the binary tables (`fontdata.js`, `outline.js`); nothing is
measured in the browser.

`verify-final.js` deliberately re-parses the shipped SVGs rather than trusting
the generator, and asserts that both treatments share byte-identical lettering.

## Files

| | |
|---|---|
| `quantos-wordmark.svg` | **the deliverable** — flat |
| `quantos-wordmark-glow.svg` | same geometry, glow treatment |
| `quantos-wordmark-final.html` | preview of both |
| `quantos-icon.svg` | master icon, background stripped for transparency |
| `BRAND-SPEC.txt` | locked ratios and rules |
| `preview.template.html` | generated comparison page template |
| `preview.html` | generated, gitignored |
## Android and browser raster icon

`android/Resources/drawable/quantos_icon.png` and `frontend/public/brand/quantos-icon.png` are
512px transparent PNG renders of `brand/quantos-icon.svg` (Chromium SVG rendering, unchanged
artwork). They identify Quantos, not the Atomic 22 publisher. The Android manifest refers to
`@drawable/quantos_icon`; keep the drawable basename stable when updating the product icon.
