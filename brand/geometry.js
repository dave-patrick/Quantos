// Single source of truth for wordmark geometry. Both the comparison preview
// and the final exported asset resolve their numbers through here, so the
// shipped SVG can never drift from what was approved.
const path = require('path');
const cfg = require('./set.js');
const { fontData } = require('./fontdata.js');

// Icon ink, measured once from the artwork (see notes in place.js).
const ICON = { vbX: 120, vbY: 120, vbH: 1020, inkX: 219.09, inkY: 219.01, inkW: 822.91, inkH: 863.99 };

const r2 = n => Math.round(n * 100) / 100;

function compute() {
  const scale = cfg.MARK / ICON.vbH;
  const inkTopOff = (ICON.inkY - ICON.vbY) * scale;
  const inkCentreOff = (ICON.inkY + ICON.inkH / 2 - ICON.vbY) * scale;
  // Ink is inset inside the artboard, so box edges are not the visible gap.
  const inkRight = (ICON.inkX + ICON.inkW - ICON.vbX) * scale;

  const built = cfg.options.map(o => {
    const d = fontData(path.join(__dirname, 'fonts', o.file), o.text[0]);
    const size = o.size + cfg.SIZE_DELTA;
    // Scale the fake-bold stroke with the type so the weight ratio holds.
    const stroke = o.stroke ? r2(o.stroke * size / o.size) : 0;
    return {
      ...o, size, stroke, darkStroke: r2(stroke * cfg.DARK_FACTOR),
      ratio: o.caps ? d.cap : d.x,
      ascRatio: d.asc,
      lsbRatio: d.lsb,
      lsb: d.lsb * size
    };
  });

  // A face flagged matchBody is sized so its body band equals the mean of
  // the others — caps otherwise read ~25% too large beside lowercase.
  const fixed = built.filter(o => !o.matchBody);
  const target = fixed.reduce((s, o) => s + o.size * o.ratio, 0) / fixed.length;
  for (const o of built.filter(o => o.matchBody)) o.size = Math.round(target / o.ratio);

  for (const o of built) {
    o.body = o.size * o.ratio;
    o.bodyMid = cfg.BASE - o.body / 2;
    o.useY = r2(o.bodyMid - inkCentreOff);
    o.inkTop = o.useY + inkTopOff;
    o.inkBot = o.useY + inkTopOff + ICON.inkH * scale;
    o.ascTop = cfg.BASE - o.size * o.ascRatio;
    // GAP is measured from the icon's ink to the first glyph's ink, so each
    // face's own side bearing gets paid off — otherwise the faces drift apart.
    o.lsb = o.lsbRatio * o.size;
    o.textX = r2(inkRight + cfg.GAP - o.lsb);
    o.spec = `${o.size}pt${o.caps ? ' caps' : ''}` +
      (o.stroke
        ? ` · stroke +${o.stroke.toFixed(1)} light / +${o.darkStroke.toFixed(1)} dark`
        : ' · no stroke');
  }

  const top = Math.min(...built.map(o => Math.min(o.inkTop, o.ascTop)));
  const bot = Math.max(...built.map(o => Math.max(o.inkBot, cfg.BASE)));
  const viewBox = `0 ${Math.round((top + bot) / 2 - cfg.FRAME_H / 2)} 900 ${cfg.FRAME_H}`;

  return { built, viewBox, scale, inkRight, inkTopOff, inkCentreOff };
}

module.exports = { compute, ICON, cfg };