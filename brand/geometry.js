// Single source of truth for wordmark geometry. Both the comparison preview
// and the final exported asset resolve their numbers through here, so the
// shipped SVG can never drift from what was approved.
const path = require('path');
const cfg = require('./set.js');
const { fontData } = require('./fontdata.js');
const { FRAME, INK } = require('./icon.js');

const r2 = n => Math.round(n * 100) / 100;

function compute(opts) {
  const flat = !!(opts && opts.flat);

  // The frame is glow-inclusive (so the halo is never sliced) but the size is
  // driven by the ink (so the Q measures against the letterform, not a halo).
  const k = cfg.MARK / INK.h;
  const boxW = FRAME.w * k;
  const boxH = FRAME.h * k;
  const inkXInBox = (INK.x - FRAME.x) * k;
  const inkCentreOff = (INK.y + INK.h / 2 - FRAME.y) * k;
  // Where the ink's right edge lands in wordmark units once the box is placed
  // with its left edge of ink at x=0. The gap is measured to this, not to the
  // box edge, because the box is larger than the ink.
  const inkRight = -inkXInBox + (INK.x + INK.w - FRAME.x) * k;

  // Extent of the glow in wordmark units, for framing the exported file.
  const glow = {
    x: -inkXInBox,
    y: 0,
    w: boxW,
    h: boxH
  };

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
    o.inkTop = o.useY + inkCentreOff - INK.h * k / 2;
    o.inkBot = o.useY + inkCentreOff + INK.h * k / 2;
    o.glowX = -inkXInBox;
    o.glowY = o.useY;
    o.glowBot = o.useY + boxH;
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

  const top = Math.min(...built.map(o => Math.min(o.inkTop, o.glowY, o.ascTop)));
  const bot = Math.max(...built.map(o => Math.max(o.inkBot, o.glowBot, cfg.BASE)));
  const viewBox = `0 ${Math.round((top + bot) / 2 - cfg.FRAME_H / 2)} 900 ${cfg.FRAME_H}`;

  return { built, viewBox, k, boxW, boxH, inkXInBox, inkCentreOff, inkRight, flat };
}

module.exports = { compute, cfg };