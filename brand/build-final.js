// Build the locked, self-contained wordmark asset.
// Text is outlined from the font binary, so the SVG renders identically with
// no webfont and no font dependency of any kind.
const fs = require('fs');
const path = require('path');
const { compute, ICON, cfg } = require('./geometry.js');
const { load, outline } = require('./outline.js');

const dir = __dirname;
const CHOICE = 'Bruno Ace SC';

const { built, scale } = compute();
const o = built.find(x => x.name === CHOICE);
if (!o) throw new Error('locked option not found: ' + CHOICE);

// ---- icon, background stripped (same treatment as the preview) ----------
const raw = fs.readFileSync(path.join(dir, 'quantos-icon.svg'), 'utf8');
const stripped = raw.replace(/\s*<!-- 1\. MAIN BACKGROUND SQUIRCLE -->[\s\S]*?<g id="Background">[\s\S]*?<\/g>/, '');
const iconBody = stripped.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '').trim();

// ---- outlined text ------------------------------------------------------
const font = load(path.join(dir, 'fonts', o.file));
const word = outline(font, o.text, o.size, o.textX, cfg.BASE);

// ---- assemble -----------------------------------------------------------
// Icon: content coords 120..1140 -> 0..120, offset down by useY.
const iconTransform = `translate(0 ${o.useY}) scale(${scale}) translate(-${ICON.vbX} -${ICON.vbY})`;

const pad = 8;
const x0 = 0;
const x1 = Math.max(word.bbox.xMax, cfg.MARK);
const y0 = Math.min(word.bbox.yMin, o.inkTop);
const y1 = Math.max(word.bbox.yMax, o.inkBot);
const r1 = n => Math.round(n * 100) / 100;
const vb = `${r1(x0 - pad)} ${r1(y0 - pad)} ${r1(x1 - x0 + pad * 2)} ${r1(y1 - y0 + pad * 2)}`;
const W = r1(x1 - x0 + pad * 2);
const H = r1(y1 - y0 + pad * 2);

function svg(ink) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${vb}" width="${W}" height="${H}" role="img" aria-label="Quantos">
  <title>Quantos</title>
  <g fill="${ink}">
    <g transform="${iconTransform}">
${iconBody.split('\n').map(l => '      ' + l).join('\n')}
    </g>
    <path d="${word.d}"/>
  </g>
</svg>
`;
}

const mono = svg('#F2F6FC');
const inverse = svg('#0B0E13');
fs.writeFileSync(path.join(dir, 'quantos-wordmark.svg'), mono.replace('#F2F6FC', 'currentColor'));

// ---- the ratio spec, so the proportions are pinned ----------------------
const capBand = o.body;
const spec = {
  wordmark: 'Q' + o.text,
  face: 'Bruno Ace SC',
  'Q size': cfg.MARK + 'u',
  'Q : cap height': (cfg.MARK / capBand).toFixed(3) + ' : 1',
  'Q : gap': (cfg.GAP / cfg.MARK).toFixed(3) + ' : 1',
  'type size': o.size + 'u',
  'cap height': capBand.toFixed(1) + 'u',
  'x offset (text origin)': o.textX + 'u',
  'baseline': cfg.BASE + 'u',
  'Q centre': o.bodyMid.toFixed(1) + 'u',
  'Q top': o.useY + 'u',
  'viewBox': vb
};

const readme =
`Quantos — locked wordmark
========================
Chosen face : ${o.name}
Letters     : ${o.text}
Built       : ${new Date().toISOString().slice(0, 10)}

Files
-----
quantos-wordmark.svg        self-contained; text outlined, fill=currentColor
quantos-wordmark-final.html preview on light/dark at several sizes

Proportions (locked)
--------------------
${Object.entries(spec).map(([k, v]) => k.padEnd(22) + v).join('\n')}

Rules
-----
1. The Q is always ${(cfg.MARK / capBand).toFixed(2)}x the cap height. Never size it independently.
2. The gap is always ${(cfg.GAP / cfg.MARK).toFixed(2)}x the Q, measured ink edge to glyph ink edge.
   The icon's ink is inset inside its artboard, so box-to-box spacing is wrong.
3. The Q's optical centre sits on the cap-height band centre.
4. Set at any size by scaling the whole group; never re-space the parts.
`;
fs.writeFileSync(path.join(dir, 'BRAND-SPEC.txt'), readme);

console.log('locked:', o.name, '-', o.text, '@', o.size);
console.log('viewBox:', vb, ' ', W + 'x' + H);
console.log('');
console.log('Q:cap height      ', (cfg.MARK / capBand).toFixed(3) + ' : 1');
console.log('gap:Q             ', (cfg.GAP / cfg.MARK).toFixed(3) + ' : 1');
console.log('outlined path len ', word.d.length);
console.log('wrote quantos-wordmark.svg, BRAND-SPEC.txt');

// ---- final preview page -------------------------------------------------
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
  .note{ color:var(--ink-dim); max-width:62ch; margin-bottom:24px }
  .card{ position:relative; border-radius:16px; padding:36px; margin-bottom:14px; background:#0B0E13; border:1px solid var(--line) }
  .card.light{ background:#F7F8FB; border-color:#0B0E1310 }
  .card .tag{ position:absolute; top:12px; right:14px; font-size:11px; color:var(--ink-faint) }
  .card.light .tag{ color:#8A94A6 }
  .card svg{ width:100%; height:auto; display:block; color:var(--ink) }
  .card.light svg{ color:#0B0E13 }
  .scale{ display:flex; align-items:flex-end; gap:28px; flex-wrap:wrap; background:#0B0E13; border:1px solid var(--line); border-radius:16px; padding:28px }
  .scale figure{ display:flex; flex-direction:column; align-items:center; gap:10px }
  .scale svg{ height:24px; width:auto; color:var(--ink) }
  .scale figcaption{ color:var(--ink-faint); font-size:11px; font-variant-numeric:tabular-nums }
  table{ border-collapse:collapse; width:100%; font-size:14px }
  th,td{ text-align:left; padding:9px 14px 9px 0; border-bottom:1px solid var(--line); vertical-align:top }
  th{ color:var(--ink-faint); font-weight:500; font-size:12px; text-transform:uppercase; letter-spacing:.06em }
  td{ color:var(--ink); font-variant-numeric:tabular-nums }
  td.k{ color:var(--ink-dim) }
  .note code{ color:#38BDF8; font-size:13px }
  footer{ border-top:1px solid var(--line); margin-top:56px; padding-top:22px; color:var(--ink-faint); font-size:13.5px }
  @media (max-width:820px){ body{padding:34px 18px 28px} h1{font-size:30px} }
</style>
</head>
<body>
<div class="wrap">
  <header>
    <p class="kicker">Quantos · brand</p>
    <h1>Wordmark locked</h1>
    <p class="lede">${o.name}, outlined. The SVG is self-contained — no webfont, no font dependency, identical on every machine.</p>
  </header>

  <section>
    <h2>The mark</h2>
    <p class="note">One path for the icon, one for the lettering. Fill follows the context via <code>currentColor</code>.</p>
    <div class="card"><span class="tag">dark</span>${mono}</div>
    <div class="card light"><span class="tag">light</span>${inverse}</div>
  </section>

  <section>
    <h2>Scale</h2>
    <p class="note">Scale the whole thing. Never re-space the parts — the gap and the Q-to-letter ratio are what hold it together.</p>
    <div class="scale">
      ${[20, 32, 48, 72, 108].map(h =>
        `<figure>${mono.replace(`height="${H}"`, `height="${h}"`)}<figcaption>${h}px</figcaption></figure>`
      ).join('\n      ')}
    </div>
  </section>

  <section>
    <h2>Locked ratios</h2>
    <p class="note">These are the numbers. Everything else is derived from them.</p>
    <table>
      <tr><th>Property</th><th>Value</th></tr>
      ${Object.entries(spec).filter(([k]) => k !== 'wordmark')
        .map(([k, v]) => `<tr><td class="k">${k}</td><td>${v}</td></tr>`).join('\n      ')}
    </table>
  </section>

  <footer>Quantos · wordmark locked · October 2026</footer>
</div>
</body>
</html>
`;
fs.writeFileSync(path.join(dir, 'quantos-wordmark-final.html'), html);
console.log('wrote quantos-wordmark-final.html');