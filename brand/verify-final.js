// Verify the exported SVG on its own terms: re-derive the geometry from the
// file's own contents and check it against the locked ratios.
const fs = require('fs');
const path = require('path');
const { compute, ICON, cfg } = require('./geometry.js');

const svg = fs.readFileSync(path.join(__dirname, 'quantos-wordmark.svg'), 'utf8');
const { built } = compute();
const o = built.find(x => x.name === 'Bruno Ace SC');

const fail = [];
const check = (ok, label, got, want) => {
  console.log((ok ? 'OK   ' : 'FAIL ') + label.padEnd(30) + String(got).padEnd(12) + 'want ' + want);
  if (!ok) fail.push(label);
};

// 1. no font dependency
check(!/@font-face|fonts\.googleapis|<text/i.test(svg), 'self-contained', 'no font/text refs', 'none');

// 2. icon transform, recomputed
const tr = (svg.match(/transform="translate\(0 ([\d.]+)\) scale\(([\d.]+)\) translate\(-120 -120\)"/) || []);
const useY = parseFloat(tr[1]), k = parseFloat(tr[2]);
check(Math.abs(useY - o.useY) < 0.01, 'icon y offset', useY, o.useY.toFixed(2));
check(Math.abs(k - cfg.MARK / ICON.vbH) < 1e-9, 'icon scale', k.toFixed(6), (cfg.MARK / ICON.vbH).toFixed(6));

// icon ink centre under that transform, vs the letter body band centre
const inkCentre = (ICON.inkY + ICON.inkH / 2 - ICON.vbY) * k + useY;
check(Math.abs(inkCentre - o.bodyMid) < 0.01, 'Q ink centre on band', inkCentre.toFixed(2), o.bodyMid.toFixed(2));

const inkRight = (ICON.inkX + ICON.inkW - ICON.vbX) * k;
const inkTop = (ICON.inkY - ICON.vbY) * k + useY;

// 3. parse the outlined path and measure it
const d = svg.match(/<path d="([^"]+)"\/>/)[1];
let x = 0, y = 0;
const bb = { xMin: Infinity, yMin: Infinity, xMax: -Infinity, yMax: -Infinity };
const see = (px, py) => {
  if (px < bb.xMin) bb.xMin = px; if (px > bb.xMax) bb.xMax = px;
  if (py < bb.yMin) bb.yMin = py; if (py > bb.yMax) bb.yMax = py;
};
const tokens = d.match(/[MLQZ]|-?[\d.]+/g);
for (let i = 0; i < tokens.length;) {
  const c = tokens[i++];
  if (c === 'M' || c === 'L') { x = +tokens[i++]; y = +tokens[i++]; see(x, y); }
  else if (c === 'Q') { see(+tokens[i], +tokens[i + 1]); x = +tokens[i + 2]; y = +tokens[i + 3]; see(x, y); i += 4; }
  else if (c === 'Z') { /* closes */ }
  else throw new Error('unexpected token ' + c);
}

const capBand = bb.yMax - bb.yMin;
check(Math.abs(capBand - o.body) < 0.05, 'cap band height', capBand.toFixed(2), o.body.toFixed(2));
check(Math.abs(bb.yMax - cfg.BASE) < 0.05, 'sits on baseline', bb.yMax.toFixed(2), cfg.BASE);

// 4. the locked ratios
const qToCap = cfg.MARK / capBand;
const gapToQ = cfg.GAP / cfg.MARK;
const gap = bb.xMin - inkRight;
check(Math.abs(gapToQ - 0.15) < 0.001, 'gap : Q', gapToQ.toFixed(3), '0.150');
check(Math.abs(qToCap - 2) < 0.01, 'Q : cap height', qToCap.toFixed(3), '~2.000');
check(Math.abs(gap - cfg.GAP) < 0.05, 'measured ink gap', gap.toFixed(2), cfg.GAP);

// 5. viewBox frames everything
const vb = svg.match(/viewBox="([^"]+)"/)[1].split(' ').map(Number);
const framed = vb[0] <= Math.min(bb.xMin, 0) && vb[1] <= Math.min(bb.yMin, inkTop) &&
               vb[0] + vb[2] >= Math.max(bb.xMax, cfg.MARK) &&
               vb[1] + vb[3] >= Math.max(bb.yMax, o.inkBot);
check(framed, 'viewBox frames all ink', vb.join(' '), 'contains everything');

console.log('\n' + (fail.length ? 'FAILURES: ' + fail.join(', ') : 'all checks passed'));