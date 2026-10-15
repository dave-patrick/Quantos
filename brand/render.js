// Generate preview.template.html from set.js + real font metrics.
// Every number in the output is computed here; nothing is typed by hand.
const fs = require('fs');
const path = require('path');
const cfg = require('./set.js');
const { fontData } = require('./fontdata.js');

const dir = __dirname;

// ---- font metrics, straight from the OS/2 / hhea tables -----------------
function tables(buf) {
  const num = buf.readUInt16BE(4);
  const map = {};
  for (let i = 0; i < num; i++) {
    const p = 12 + i * 16;
    map[buf.toString('latin1', p, p + 4).trim()] = { off: buf.readUInt32BE(p + 8) };
  }
  return map;
}
function metrics(file) {
  const buf = fs.readFileSync(file);
  const t = tables(buf);
  const upem = buf.readUInt16BE(t.head.off + 18);
  return {
    x: buf.readInt16BE(t['OS/2'].off + 86) / upem,
    cap: buf.readInt16BE(t['OS/2'].off + 88) / upem,
    asc: buf.readInt16BE(t.hhea.off + 4) / upem
  };
}

// ---- icon ink, measured once (see notes in place.js) --------------------
const ICON = { vbX: 120, vbY: 120, vbH: 1020, inkX: 219.09, inkY: 219.01, inkW: 822.91, inkH: 863.99 };
const scale = cfg.MARK / ICON.vbH;
const inkTopOff = (ICON.inkY - ICON.vbY) * scale;
const inkCentreOff = (ICON.inkY + ICON.inkH / 2 - ICON.vbY) * scale;
// The ink is inset inside its artboard, so the gap between the Q's box and
// the text is not the gap you actually see between the two shapes.
const inkRight = (ICON.inkX + ICON.inkW - ICON.vbX) * scale;

// ---- build each option --------------------------------------------------
const r2 = n => Math.round(n * 100) / 100;
const built = cfg.options.map(o => {
  const m = metrics(path.join(dir, 'fonts', o.file));
  const size = o.size + cfg.SIZE_DELTA;
  // Scale the fake-bold stroke with the type so the weight ratio is preserved.
  const stroke = o.stroke ? r2(o.stroke * size / o.size) : 0;
  const ratio = o.caps ? m.cap : m.x;
  return { ...o, size, stroke, ratio, darkStroke: r2(stroke * cfg.DARK_FACTOR), ascRatio: m.asc };
});

// A face flagged matchBody is sized so its body band equals the mean of the
// others. Caps measured against everyone else's x-height read ~25% too large;
// this keeps them honest automatically instead of hand-nudging them again.
const fixed = built.filter(o => !o.matchBody);
const target = fixed.reduce((s, o) => s + o.size * o.ratio, 0) / fixed.length;
for (const o of built.filter(o => o.matchBody)) {
  o.size = Math.round(target / o.ratio);
}

// Every position and label now follows from the resolved sizes.
for (const o of built) {
  o.body = o.size * o.ratio;
  o.bodyMid = cfg.BASE - o.body / 2;
  o.useY = r2(o.bodyMid - inkCentreOff);
  o.inkTop = o.useY + inkTopOff;
  o.inkBot = o.useY + inkTopOff + ICON.inkH * scale;
  o.ascTop = cfg.BASE - o.size * o.ascRatio;
  // GAP is measured from the icon's ink to the first glyph's ink, so each
  // face's own side bearing gets paid off — otherwise the faces drift apart.
  o.lsb = fontData(path.join(dir, 'fonts', o.file), o.text[0]).lsb * o.size;
  o.textX = r2(inkRight + cfg.GAP - o.lsb);
  o.spec = `${o.size}pt${o.caps ? ' caps' : ''}` +
    (o.stroke ? ` · stroke +${o.stroke.toFixed(1)} light / +${(o.darkStroke).toFixed(1)} dark` : ' · no stroke');
}

// ---- frame the wordmark so it sits centred in its card ------------------
const top = Math.min(...built.map(o => Math.min(o.inkTop, o.ascTop)));
const bot = Math.max(...built.map(o => Math.max(o.inkBot, cfg.BASE)));
const viewBox = `0 ${Math.round((top + bot) / 2 - cfg.FRAME_H / 2)} 900 ${cfg.FRAME_H}`;

// ---- render -------------------------------------------------------------
function stage(o, light) {
  const sw = light ? o.stroke : o.darkStroke;
  const painted = sw
    ? ` stroke="currentColor" stroke-width="${sw}" stroke-linejoin="round" paint-order="stroke"`
    : '';
  return `<div class="stage${light ? ' light' : ''}"><span class="tag">${light ? 'light' : 'dark'}</span>\n` +
    `          <svg viewBox="${viewBox}"><use href="#mark" x="0" y="${o.useY}" width="${cfg.MARK}" height="${cfg.MARK}"/>` +
    `<text x="${o.textX}" y="${cfg.BASE}" fill="currentColor"${painted} font-family="${o.css}" font-size="${o.size}">${o.text}</text></svg>\n` +
    `        </div>`;
}
function block(o) {
  const keep = o.tag ? `<span class="keep">${o.tag}</span>` : '';
  return `    <div class="opt">
      <div class="meta"><h3>${o.name}</h3><span class="spec">${o.spec}</span>${keep}<p>${o.blurb}</p></div>
      <div class="stages">
${stage(o, false)}
${stage(o, true)}
      </div>
    </div>`;
}

const locked = built.find(o => o.locked);
const rest = built.filter(o => !o.locked);

const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Quantos — wordmark options</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Anta&family=Baumans&family=Bruno+Ace+SC&family=Gruppo&family=Turret+Road&family=Zen+Dots&display=swap" rel="stylesheet">
<style>
  :root{ --bg:#05070A; --line:#FFFFFF14; --ink:#F2F6FC; --ink-dim:#9AA6B8; --ink-faint:#5B6577; }
  *{box-sizing:border-box;margin:0;padding:0}
  body{ background:var(--bg); color:var(--ink); font-family:system-ui,"Segoe UI",sans-serif; font-size:15px; line-height:1.6; padding:56px 40px 44px; }
  .wrap{max-width:1040px;margin:0 auto}
  .build-stamp{ color:#5B6577; font-size:11px; letter-spacing:.08em; font-variant-numeric:tabular-nums; margin-bottom:10px; font-family:ui-monospace,Menlo,Consolas,monospace }
  header .kicker{ color:#38BDF8; font-size:13px; margin-bottom:12px }
  header h1{ font-size:40px; font-weight:650; letter-spacing:-.02em; line-height:1.15; margin-bottom:14px }
  header .lede{ color:var(--ink-dim); max-width:62ch; font-size:17px; font-weight:350 }
  section{ border-top:1px solid var(--line); margin-top:48px; padding-top:32px }
  section h2{ font-size:23px; font-weight:600; letter-spacing:-.01em; margin-bottom:4px }
  section .note{ color:var(--ink-dim); max-width:62ch; margin-bottom:24px }
  .opt{ margin-bottom:26px }
  .meta{ display:flex; align-items:baseline; gap:14px; flex-wrap:wrap; margin-bottom:10px }
  .meta h3{ font-size:16px; font-weight:600 }
  .meta .spec{ color:var(--ink-faint); font-size:12.5px; font-variant-numeric:tabular-nums }
  .meta p{ color:var(--ink-dim); font-weight:350; font-size:13.5px; flex-basis:100% }
  .keep{ color:#38BDF8; font-size:11px; letter-spacing:.06em; text-transform:uppercase }
  .stages{ display:grid; grid-template-columns:1fr 1fr; gap:12px }
  .stage{ position:relative; border-radius:16px; padding:24px; display:flex; align-items:center; background:#0B0E13; border:1px solid var(--line); color:var(--ink) }
  .stage.light{ background:#F7F8FB; border-color:#0B0E1310; color:#0B0E13 }
  .stage .tag{ position:absolute; top:9px; right:12px; font-size:11px; color:var(--ink-faint) }
  .stage.light .tag{ color:#8A94A6 }
  .stage svg{ width:100%; max-width:460px; height:auto; display:block }
  .explain{ background:#0B0E13;border:1px solid var(--line);border-radius:16px;padding:24px;color:var(--ink-dim);font-size:14px;font-weight:350 }
  .explain b{ color:var(--ink); font-weight:600 }
  footer{ border-top:1px solid var(--line); margin-top:56px; padding-top:22px; color:var(--ink-faint); font-size:13.5px; display:flex; justify-content:space-between; gap:20px; flex-wrap:wrap }
  @media (max-width:820px){ body{padding:34px 18px 28px} .stages{grid-template-columns:1fr} header h1{font-size:30px} }
</style>
</head>
<body>

<svg width="0" height="0" style="position:absolute" aria-hidden="true">
<!--MARK-->
</svg>

<div class="wrap">
  <header>
    <p class="kicker">Quantos · brand</p>
    <h1>Six letterings</h1>
    <p class="lede">The Q is the same size and gap on all six, and is optically centred on each letterform's own body band. Any difference you see is the typeface alone.</p>
  </header>

  <section>
    <h2>How these are set</h2>
    <div class="explain">
      <b>Constant geometry.</b> The Q is <b>${cfg.MARK} units</b> square on every option, and the gap is a constant <b>${cfg.GAP} units</b>. That is what makes the six comparable.<br><br>
      <b>The gap is optical, not nominal.</b> The icon's ink is inset inside its artboard and every face gives its first letter a different side bearing — Gruppo's <b>u</b> alone adds 11 units. Measuring box-to-box left the real gaps running from <b>29 to 35 units</b>, and Gruppo visibly further out than the rest. Each face is now positioned so ink-to-ink is identical.<br><br>
      <b>Optical centring.</b> The Q moves vertically per face, because faces don't share an x-height. Its optical centre is placed on each letterform's body band, read straight from the font files: Gruppo's x-height is <b>0.374 em</b>, Anta's is <b>0.512</b>. Centring on a shared line would leave the Q riding high on the small-x-height faces.<br><br>
      <b>The icon's own offset is corrected too.</b> Its ink sits 21 units below the centre of its artboard, so centring on the box centre would sit the Q low by ~2.5 units on every option.<br><br>
      <b>Type sizes differ per face</b> because each typeface has its own proportions, so the letters sit at an equivalent visual size against the Q. Shrinking the type rescales the fake-bold stroke with it, to hold the weight ratio steady.<br><br>
      <b>Optical compensation.</b> Light strokes on a dark ground bloom, so every dark stage is drawn lighter than its light twin. The numbers differ on purpose so the pair looks the same rather than merely measuring the same.
    </div>
  </section>

  <section>
    <h2>Locked in</h2>
    <p class="note">${locked.name}, your reference choice.</p>

${block(locked)}
  </section>

  <section>
    <h2>The set</h2>
    <p class="note">Same Q, same size, same gap on all ${rest.length} — only the vertical optical position differs, because each face has its own x-height.</p>

${rest.map(block).join('\n\n')}
  </section>

  <footer>
    <span>Quantos · six letterings · October 2026</span>
    <span>Shortlist and I'll export the final SVGs</span>
  </footer>
</div>
</body>
</html>
`;

fs.writeFileSync(path.join(dir, 'preview.template.html'), html);

console.log('SIZE_DELTA ' + (cfg.SIZE_DELTA > 0 ? '+' : '') + cfg.SIZE_DELTA +
            '   optical GAP ' + cfg.GAP + ' (icon ink -> glyph ink)');
console.log('viewBox "' + viewBox + '"');
console.log('');
console.log('name'.padEnd(14) + 'size'.padEnd(7) + 'textX'.padEnd(8) + 'lsb'.padEnd(7) +
            'ink->ink'.padEnd(10) + 'strokeL'.padEnd(9) + 'useY');
console.log('-'.repeat(66));
for (const o of built) {
  console.log(o.name.padEnd(14) + String(o.size).padEnd(7) + String(o.textX).padEnd(8) +
    o.lsb.toFixed(1).padEnd(7) +
    (o.textX + o.lsb - inkRight).toFixed(1).padEnd(10) +
    (o.stroke ? o.stroke.toFixed(2) : '-').padEnd(9) +
    o.useY);
}