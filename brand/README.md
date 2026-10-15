# Quantos brand

The wordmark is the app icon used as the letter **Q**, followed by `uantos`.

**Locked:** Bruno Ace SC, caps, outlined. See [`BRAND-SPEC.txt`](BRAND-SPEC.txt) for
the ratios and the rules that hold it together.

## Use it

`quantos-wordmark.svg` is the deliverable. It is self-contained — the lettering is
outlined from the font binary, so there is no webfont or font dependency of any
kind. It fills from `currentColor`, so it inherits whatever colour you set.

```html
<svg viewBox="-8 111.08 605.83 117.65" style="color:#F2F6FC"><!-- contents --></svg>
```

`quantos-wordmark-final.html` is a preview: light and dark, plus a scale strip.

## The two ratios

| | ratio | absolute |
|---|---|---|
| Q : cap height | 1.993 : 1 | 120 / 60.2 |
| gap : Q | 0.150 : 1 | 18 / 120 |

Three things that are easy to get wrong, and are corrected automatically here:

- **The icon's ink is inset inside its artboard** — it sits 21 units below the
  centre of its 1020-unit box. Spacing measured box-to-box is wrong, and
  centring on the box centre puts the Q low.
- **Faces don't share an x-height.** Gruppo's is 0.374 em, Anta's 0.512. The Q
  has to move vertically per face to stay centred on the letterform's body band.
- **Every face gives its first letter a different side bearing** — Gruppo's `u`
  adds 11 units, Turret Road's 5.4. The gap is measured ink-edge to glyph-ink,
  so the bearing is paid off or the faces drift apart.

## Rebuilding

```sh
node fetch-fonts.js     # fonts/ is gitignored; third-party binaries
node render.js          # -> preview.template.html
node build-preview.js   # -> preview.html          (six-option comparison)
node build-final.js     # -> quantos-wordmark.svg  (the deliverable)
                        #    quantos-wordmark-final.html
                        #    BRAND-SPEC.txt
node verify-geom.js     # structure of the comparison page
node verify-final.js    # re-derives the ratios from the exported SVG itself
```

`set.js` holds the tunables — `SIZE_DELTA`, `GAP`, `MARK`, and the per-face
sizes. Everything downstream is computed from real font metrics read out of the
binary tables (`fontdata.js`, `outline.js`); nothing is measured in the browser.

`verify-final.js` deliberately re-parses the shipped SVG rather than trusting
the generator, so a drift between the two cannot pass silently.

## Files

| | |
|---|---|
| `quantos-wordmark.svg` | the deliverable |
| `quantos-wordmark-final.html` | preview of the deliverable |
| `quantos-icon.svg` | master icon, background stripped for transparency |
| `BRAND-SPEC.txt` | locked ratios and rules |
| `preview.template.html` | generated comparison page template |
| `preview.html` | generated, gitignored |