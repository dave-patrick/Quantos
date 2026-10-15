// Build the locked, self-contained wordmark assets.
// Text is outlined from the font binary, so the SVGs render identically with
// no webfont and no font dependency of any kind.
//
// Two treatments are produced from the same artwork:
//   glow - the icon as designed: ambient glow plus drop shadows
//   flat - the 3D treatment stripped, nothing but the shapes
// Both use identical ink sizing and an identical optical gap, so the only
// difference between them is the treatment. The choice is still open.
const fs = require('fs');
const path = require('path');
const { compute, cfg } = require('./geometry.js');
const { load, outline } = require('./outline.js');
const { loadIcon, frameFor } = require('./icon.js');

const dir = __dirname;
const CHOICE = 'Bruno Ace SC';

const r1 = n => Math.round(n * 100) / 100;

function build(flat) {
  const G = compute({ flat });
  const { built, k, boxW, boxH } = G;
  const o = built.find(x => x.name === CHOICE);
  if (!o) throw new Error('locked option not found: ' + CHOICE);

  const icon = loadIcon(path.join(dir, 'quantos-icon.svg'), { flat });
  const FRAME = frameFor(flat);

  const font = load(path.join(dir, 'fonts', o.file));
  const word = outline(font, o.text, o.size, o.textX, cfg.BASE);

  // Icon content coords FRAME -> the box placed so its ink sits at x=0 and its
  // ink centre lands on the letter body band.
  const transform = `translate(${r1(o.glowX)} ${r1(o.glowY)}) scale(${k}) translate(-${FRAME.x} -${FRAME.y})`;

  // Frame on the glow, not the ink — otherwise the halo gets sliced and leaves
  // hard straight edges on whichever ground the mark is placed on.
  const pad = 4;
  const x0 = Math.min(0, o.glowX);
  const x1 = Math.max(word.bbox.xMax, o.glowX + boxW);
  const y0 = Math.min(word.bbox.yMin, o.glowY);
  const y1 = Math.max(word.bbox.yMax, o.glowBot);
  const vb = `${r1(x0 - pad)} ${r1(y0 - pad)} ${r1(x1 - x0 + pad * 2)} ${r1(y1 - y0 + pad * 2)}`;
  const W = r1(x1 - x0 + pad * 2);
  const H = r1(y1 - y0 + pad * 2);

  const svg = ink => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${vb}" width="${W}" height="${H}" role="img" aria-label="Quantos">
  <title>Quantos</title>
  <g fill="${ink}">
    <g transform="${transform}">
${icon.body.split('\n').map(l => '      ' + l).join('\n')}
    </g>
    <path d="${word.d}"/>
  </g>
</svg>
`;

  const capBand = word.bbox.yMax - word.bbox.yMin;
  const spec = {
    treatment: flat ? 'flat' : 'glow',
    face: o.name,
    'Q ink height': cfg.MARK + 'u',
    'Q box (incl. glow)': boxW.toFixed(2) + ' x ' + boxH.toFixed(2) + 'u',
    'Q : cap height': (cfg.MARK / capBand).toFixed(3) + ' : 1',
    'Q : gap': (cfg.GAP / cfg.MARK).toFixed(3) + ' : 1',
    'type size': o.size + 'u',
    'cap height': capBand.toFixed(1) + 'u',
    'text origin x': o.textX + 'u',
    baseline: cfg.BASE + 'u',
    'Q ink centre': o.bodyMid.toFixed(1) + 'u',
    viewBox: vb
  };

  return { o, word, capBand, boxW, boxH, vb, W, H, spec, flat,
           mono: svg('#F2F6FC'), inverse: svg('#0B0E13') };
}

// Two treatments are built from the same artwork:
//   flat - the 3D treatment stripped, nothing but the shapes
//   glow - the icon as designed: ambient glow plus drop shadows
// They use identical ink sizing and an identical optical gap, so the only
// difference between them is the treatment. cfg.TREATMENT decides which one
// ships as the wordmark; the other is kept alongside as a named alternative.
const chosen = compute({ flat: cfg.TREATMENT === 'flat' });
const other = compute({ flat: cfg.TREATMENT !== 'flat' });

const primary = build(cfg.TREATMENT === 'flat');
const alternate = build(cfg.TREATMENT !== 'flat');

// The canonical filename always holds the chosen asset.
fs.writeFileSync(path.join(dir, 'quantos-wordmark.svg'),
  primary.mono.replace('#F2F6FC', 'currentColor'));

// Clear both alternative names before writing, so switching TREATMENT cannot
// leave the previously-named file behind.
const ALT_NAMES = ['flat', 'glow'];
for (const n of ALT_NAMES) {
  const p = path.join(dir, `quantos-wordmark-${n}.svg`);
  if (n !== (alternate.flat ? 'flat' : 'glow') && fs.existsSync(p)) fs.unlinkSync(p);
}
fs.writeFileSync(path.join(dir, `quantos-wordmark-${alternate.flat ? 'flat' : 'glow'}.svg`),
  alternate.mono.replace('#F2F6FC', 'currentColor'));

for (const v of [primary, alternate]) {
  const file = v === primary ? 'quantos-wordmark.svg'
    : `quantos-wordmark-${v.flat ? 'flat' : 'glow'}.svg`;
  console.log(`${(v.flat ? 'flat' : 'glow').padEnd(5)}${v === primary ? ' <- chosen' : ' <- alt   '}  ` +
    `${file.padEnd(30)} viewBox ${v.vb.padEnd(28)} ${v.W}x${v.H}  ` +
    `Q:cap ${(cfg.MARK / v.capBand).toFixed(3)}:1  gap:Q ${(cfg.GAP / cfg.MARK).toFixed(3)}:1`);
}
console.log('wrote quantos-wordmark.svg, quantos-wordmark-' +
  (alternate.flat ? 'flat' : 'glow') + '.svg, BRAND-SPEC.txt, quantos-wordmark-final.html');

const glow = primary.flat ? alternate : primary;
const flat = primary.flat ? primary : alternate;

// ---- the ratio spec ------------------------------------------------------
const capBand = primary.capBand;
const readme = `Quantos — locked wordmark
========================
Chosen face : ${CHOICE}
Letters     : ${primary.o.text}
Treatment   : ${cfg.TREATMENT}${primary.flat ? ' (3D treatment removed)' : ' (as designed)'}
Built       : ${new Date().toISOString().slice(0, 10)}

Files
-----
quantos-wordmark.svg        THE WORDMARK. Self-contained, fill=currentColor.
quantos-wordmark-${alternate.flat ? 'flat' : 'glow'}.svg   the other treatment, same geometry
quantos-wordmark-final.html preview of both, on light and dark

Proportions
-----------
${Object.entries(primary.spec).filter(([k]) => k !== 'treatment')
  .map(([k, v]) => k.padEnd(22) + v).join('\n')}

Identical in both treatments. They differ only in treatment - same ink size,
same optical gap, same centring, byte-identical lettering.

Rules
-----
1. The Q is always ${(cfg.MARK / capBand).toFixed(2)}x the cap height. Never size it independently.
2. The gap is always ${(cfg.GAP / cfg.MARK).toFixed(2)}x the Q, measured ink edge to glyph ink edge.
   The icon's glow is inset, so box-to-box spacing is wrong.
3. The Q's optical centre sits on the cap-height band centre.
4. Set at any size by scaling the whole group; never re-space the parts.

Why flat
--------
The glow was drawn for a 1024px app icon. In a wordmark it carries a faint
grey cast on light grounds and dissolves at small sizes, which is most of
where a wordmark gets used. Flat's box is also exactly the ink, so there is
no hidden margin to misjudge.

Note on measurement
-------------------
Ink and glow extents come from pixel scans of the rendered icon, not getBBox.
getBBox reports geometry only and excludes stroke, and the arcs carry
stroke-width 128 - it understated the mark by 64 units a side.
`;
fs.writeFileSync(path.join(dir, 'BRAND-SPEC.txt'), readme);

// ---- preview page --------------------------------------------------------
const card = (v, kind) =>
  `<div class="card${kind === 'light' ? ' light' : ''}"><span class="tag">${kind}</span>${
    kind === 'light' ? v.inverse : v.mono}</div>`;

const strip = v => [20, 32, 48, 72, 108].map(h =>
  `<figure>${v.mono.replace(`height="${v.H}"`, `height="${h}"`)}<figcaption>${h}px</figcaption></figure>`
).join('\n        ');

const table = v => Object.entries(v.spec)
  .map(([k, val]) => `<tr><td class="k">${k}</td><td>${val}</td></tr>`).join('\n        ');

const section = (v, title, note, heading) => `
  <section>
    <h2>${heading || title}</h2>
    <p class="note">${note}</p>
${card(v, 'dark')}
${card(v, 'light')}
    <h3>Scale</h3>
    <div class="scale">
        ${strip(v)}
    </div>
  </section>`;

const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Quantos — locked wordmark</title>
<style>
  :root{ --bg:#05070A; --line:#FFFFFF14; --ink:#F2F6FC; --ink-dim:#9AA6B8; --ink-faint:#5B6577; }
  *{box-sizing:border-box;margin:0;padding:0}
  body{ background:var(--bg); color:var(--ink); font-family:system-ui,"Segoe UI",sans-serif; font-size:15px; line-height:1.6; padding:56px 40px 44px }
  .wrap{max-width:1040px;margin:0 auto}
  .kicker{ color:#38BDF8; font-size:13px; margin-bottom:12px }
  h1{ font-size:40px; font-weight:650; letter-spacing:-.02em; line-height:1.15; margin-bottom:14px }
  .lede{ color:var(--ink-dim); max-width:62ch; font-size:17px; font-weight:350 }
  section{ border-top:1px solid var(--line); margin-top:48px; padding-top:32px }
  h2{ font-size:23px; font-weight:600; letter-spacing:-.01em; margin-bottom:4px }
  h3{ font-size:15px; font-weight:600; margin:26px 0 10px; color:#38BDF8; letter-spacing:.04em; text-transform:uppercase }
  .note{ color:var(--ink-dim); max-width:62ch; margin-bottom:24px }
  .locked{ display:inline-block; background:#38BDF81F; color:#38BDF8; font-size:11px; letter-spacing:.08em; text-transform:uppercase; padding:3px 9px; border-radius:999px; margin-left:10px; vertical-align:middle }
  .card{ position:relative; border-radius:16px; padding:36px; margin-bottom:14px; background:#0B0E13; border:1px solid var(--line) }
  .card.light{ background:#F7F8FB; border-color:#0B0E1310 }
  .card .tag{ position:absolute; top:12px; right:14px; font-size:11px; color:var(--ink-faint) }
  .card.light .tag{ color:#8A94A6 }
  .card svg{ width:100%; height:auto; display:block; color:var(--ink) }
  .card.light svg{ color:#0B0E13 }
  .scale{ display:flex; align-items:flex-end; gap:28px; flex-wrap:wrap; background:#0B0E13; border:1px solid var(--line); border-radius:16px; padding:28px; margin-bottom:14px }
  .scale figure{ display:flex; flex-direction:column; align-items:center; gap:10px }
  .scale svg{ height:24px; width:auto; color:var(--ink) }
  .scale figcaption{ color:var(--ink-faint); font-size:11px; font-variant-numeric:tabular-nums }
  .explain{ background:#0B0E13;border:1px solid var(--line);border-radius:16px;padding:24px;color:var(--ink-dim);font-size:14px;font-weight:350 }
  .explain b{ color:var(--ink); font-weight:600 }
  table{ border-collapse:collapse; width:100%; font-size:14px }
  th,td{ text-align:left; padding:9px 14px 9px 0; border-bottom:1px solid var(--line); vertical-align:top }
  th{ color:var(--ink-faint); font-weight:500; font-size:12px; text-transform:uppercase; letter-spacing:.06em }
  td{ color:var(--ink); font-variant-numeric:tabular-nums }
  td.k{ color:var(--ink-dim) }
  code{ background:#0B0E13; border:1px solid var(--line); border-radius:6px; padding:2px 7px; font-size:13px; color:#38BDF8 }
  footer{ border-top:1px solid var(--line); margin-top:56px; padding-top:22px; color:var(--ink-faint); font-size:13.5px }
  @media (max-width:820px){ body{padding:34px 18px 28px} h1{font-size:30px} }
</style>
</head>
<body>
<div class="wrap">
  <header>
    <p class="kicker">Quantos · brand</p>
    <h1>Wordmark locked</h1>
    <p class="lede">${CHOICE}, outlined, flat treatment. The SVG is self-contained — no webfont, no font dependency, identical on every machine. Use <code>quantos-wordmark.svg</code>.</p>
  </header>

${section(primary, '', `The shipped wordmark: <code>quantos-wordmark.svg</code>. Shapes only — the glow and drop shadows are removed. Its box is exactly the ink, so there is no hidden margin to misjudge.`, 'The wordmark')}
${section(alternate, '', `Kept as <code>quantos-wordmark-${alternate.flat ? 'flat' : 'glow'}.svg</code>. Identical lettering, gap and centring; only the treatment differs. Not the shipped asset.`, `The other treatment: ${alternate.flat ? 'flat' : 'glow'}`)}

  <section>
    <h2>Proportions</h2>
    <p class="note">Identical in both treatments. These are the numbers; everything else derives from them.</p>
    <table>
      <tr><th>Property</th><th>Shipped (${cfg.TREATMENT})</th><th>${alternate.flat ? 'flat' : 'glow'}</th></tr>
      ${Object.keys(primary.spec).filter(k => k !== 'treatment')
        .map(k => `<tr><td class="k">${k === 'Q box (incl. glow)'
            ? (cfg.TREATMENT === 'flat' ? 'Q box' : 'Q box (incl. glow)')
            : k}</td><td>${primary.spec[k]}</td><td>${alternate.spec[k]}</td></tr>`).join('\n      ')}
    </table>
  </section>

  <section>
    <h2>Rules</h2>
    <div class="explain">
      <b>1.</b> The Q is always <b>${(cfg.MARK / capBand).toFixed(2)}x</b> the cap height. Never size it independently.<br><br>
      <b>2.</b> The gap is always <b>${(cfg.GAP / cfg.MARK).toFixed(2)}x</b> the Q, measured ink edge to glyph ink edge. Measuring box-to-box is wrong.<br><br>
      <b>3.</b> The Q's optical centre sits on the cap-height band centre.<br><br>
      <b>4.</b> Scale the whole group at any size. Never re-space the parts.
    </div>
  </section>

  <footer>Quantos · wordmark locked · October 2026</footer>
</div>
</body>
</html>
`;
fs.writeFileSync(path.join(dir, 'quantos-wordmark-final.html'), html);

