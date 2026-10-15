// Verify the exported SVG on its own terms: re-derive the geometry from the
// file's own contents and check it against the locked ratios.
const fs = require('fs');
const path = require('path');
const { compute, cfg } = require('./geometry.js');
const { INK, frameFor } = require('./icon.js');

const svg = fs.readFileSync(path.join(__dirname, 'quantos-wordmark.svg'), 'utf8');
const { built, k, boxW, boxH } = compute({ flat: cfg.TREATMENT === 'flat' });
const FRAME = frameFor(cfg.TREATMENT === 'flat');
const o = built.find(x => x.name === 'Bruno Ace SC');

const fail = [];
const check = (ok, label, got, want) => {
  console.log((ok ? 'OK   ' : 'FAIL ') + label.padEnd(30) + String(got).padEnd(12) + 'want ' + want);
  if (!ok) fail.push(label);
};

// 1. no font dependency
check(!/@font-face|fonts\.googleapis|<text/i.test(svg), 'self-contained', 'no font/text refs', 'none');

// 1b. no mojibake - a PowerShell -Encoding UTF8 rewrite once re-decoded every
//     em dash and middle dot in the generator, and it reached a shipped asset
//     unnoticed. Guard every file we publish.
const published = ['quantos-wordmark.svg', 'quantos-wordmark-flat.svg',
                   'quantos-wordmark-glow.svg', 'quantos-wordmark-final.html',
                   'BRAND-SPEC.txt']
  .filter(f => f !== 'quantos-wordmark-flat.svg' || fs.existsSync(path.join(__dirname, 'quantos-wordmark-flat.svg')))
  .map(f => [f, fs.readFileSync(path.join(__dirname, f), 'utf8')]);
const MOJI = /[ÂÃâ][-¿€”]/.exec(published.map(([, s]) => s).join(''));
check(!MOJI, 'no mojibake in published files',
  MOJI ? `${MOJI[0]} in ` + published.find(([, s]) => s.includes(MOJI[0]))[0] : 'clean', 'clean');

// 2. icon transform, recomputed
const tr = (svg.match(/transform="translate\((-?[\d.]+) (-?[\d.]+)\) scale\(([\d.]+)\) translate\(-(\d+) -(\d+)\)"/) || []);
const tx = parseFloat(tr[1]), ty = parseFloat(tr[2]), kk = parseFloat(tr[3]);
check(Math.abs(kk - k) < 1e-9, 'icon scale', kk.toFixed(6), k.toFixed(6));
check(Math.abs(ty - o.glowY) < 0.01, 'icon y offset', ty, o.glowY.toFixed(2));
check(Math.abs(tx - o.glowX) < 0.01, 'icon x offset', tx, o.glowX.toFixed(2));

// ink left edge must land exactly on 0, ink centre on the body band
const inkLeft = tx + (INK.x - FRAME.x) * kk;
const inkCentre = ty + (INK.y + INK.h / 2 - FRAME.y) * kk;
check(Math.abs(inkLeft) < 0.01, 'ink left at x=0', inkLeft.toFixed(2), '0.00');
check(Math.abs(inkCentre - o.bodyMid) < 0.01, 'ink centre on band', inkCentre.toFixed(2), o.bodyMid.toFixed(2));

const inkRight = tx + (INK.x + INK.w - FRAME.x) * kk;
const glowBox = { x: tx, y: ty, w: boxW, h: boxH };

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

// 5. viewBox must contain the glow, not just the ink
const vb = svg.match(/viewBox="([^"]+)"/)[1].split(' ').map(Number);
const framed = vb[0] <= glowBox.x && vb[1] <= glowBox.y &&
               vb[0] + vb[2] >= glowBox.x + glowBox.w &&
               vb[1] + vb[3] >= glowBox.y + glowBox.h;
check(framed, 'viewBox contains mark', vb.join(' '),
  `mark ${glowBox.x.toFixed(1)},${glowBox.y.toFixed(1)} ${glowBox.w.toFixed(1)}x${glowBox.h.toFixed(1)}`);

// 6. the shipped asset must be the chosen treatment, and the other must exist
const altName = cfg.TREATMENT === 'flat' ? 'glow' : 'flat';
const altPath = path.join(__dirname, `quantos-wordmark-${altName}.svg`);
const altSvg = fs.existsSync(altPath) ? fs.readFileSync(altPath, 'utf8') : '';
check(!!altSvg, `alternative ${altName} exists`, altSvg ? 'yes' : 'MISSING', 'yes');
check(cfg.TREATMENT === 'flat'
  ? !/<filter|filter="url\(/.test(svg)
  : /<filter/.test(svg), 'primary is ' + cfg.TREATMENT,
  /<filter/.test(svg) ? 'glow' : 'flat', cfg.TREATMENT);

const fg = compute({ flat: cfg.TREATMENT === 'flat' });
if (cfg.TREATMENT === 'flat') {
  check(Math.abs(fg.boxW - cfg.MARK) < 0.01, 'flat box == ink == MARK', fg.boxW.toFixed(2), String(cfg.MARK));
}

// 7. both variants must share identical type geometry and gap
const dOf = s => (s.match(/<path d="([^"]+)"\/>/) || [])[1];
check(!!altSvg && dOf(altSvg) === dOf(svg), 'lettering identical',
  dOf(svg).length + ' chars', 'same');
check(Math.abs(fg.inkRight - inkRight) < 0.01, 'gap identical across variants',
  fg.inkRight.toFixed(2), inkRight.toFixed(2));
check(Math.abs(compute({ flat: !cfg.TREATMENT.includes('flat') }).built
        .find(x => x.name === 'Bruno Ace SC').textX - o.textX) < 0.01,
  'text origin identical', o.textX, o.textX);

console.log('\n' + (fail.length ? 'FAILURES: ' + fail.join(', ') : 'all checks passed'));