// Structural check on the generated HTML: mark size, centring, and the
// optical gap between the icon's ink and each face's first glyph.
const fs = require('fs');
const path = require('path');
const { fontData } = require('./fontdata.js');

const h = fs.readFileSync(path.join(__dirname, 'preview.html'), 'utf8');

// Same constants render.js uses.
const ICON = { vbX: 120, inkX: 219.09, inkW: 822.91 };
const scale = 120 / 1020;
const inkRight = (ICON.inkX + ICON.inkW - ICON.vbX) * scale;

const opts = h.split('<div class="opt">').slice(1);
let bad = 0;
const gaps = [];

console.log('name'.padEnd(14) + 'markW'.padEnd(7) + 'markY'.padEnd(9) + 'textX'.padEnd(8) + 'baseY'.padEnd(8) + 'gap(ink-ink)'.padEnd(13) + 'pair');
console.log('-'.repeat(70));

for (const block of opts) {
  const name = (block.match(/<h3>([^<]+)<\/h3>/) || [])[1] || '?';
  const svgs = block.match(/<svg viewBox="[^"]+">[\s\S]*?<\/svg>/g) || [];
  if (svgs.length !== 2) { console.log(name + ': ' + svgs.length + ' stages'); bad++; continue; }

  const read = (svg, tag, attr) => {
    const m = (svg.match(new RegExp('<' + tag + '[^>]*>')) || [''])[0];
    const r = m.match(new RegExp(attr + '="([^"]*)"'));
    return r ? r[1] : null;
  };
  const d = { w: read(svgs[0], 'use', 'width'), y: read(svgs[0], 'use', 'y'),
              tx: read(svgs[0], 'text', 'x'), by: read(svgs[0], 'text', 'y'),
              fs: read(svgs[0], 'text', 'font-size'), fam: read(svgs[0], 'text', 'font-family') };
  const l = { w: read(svgs[1], 'use', 'width'), y: read(svgs[1], 'use', 'y'),
              tx: read(svgs[1], 'text', 'x'), by: read(svgs[1], 'text', 'y') };

  // pair = dark and light stages describe the same wordmark
  const pair = d.w === l.w && d.y === l.y && d.tx === l.tx && d.by === l.by ? 'ok' : 'MISMATCH';
  if (pair !== 'ok') bad++;

  const size = parseFloat(d.fs);
  const file = { 'Gruppo': 'Gruppo.ttf', 'Anta': 'Anta.ttf', 'Baumans': 'Baumans.ttf',
                 'Turret Road': 'Turret_Road.ttf', 'Zen Dots': 'Zen_Dots.ttf',
                 'Bruno Ace SC': 'Bruno_Ace_SC.ttf' }[name];
  const first = name === 'Bruno Ace SC' ? 'U' : 'u';
  const lsb = fontData(path.join(__dirname, 'fonts', file), first).lsb * size;
  const gap = parseFloat(d.tx) + lsb - inkRight;
  gaps.push(+gap.toFixed(2));

  console.log(name.padEnd(14) + d.w.padEnd(7) + d.y.padEnd(9) + d.tx.padEnd(8) + d.by.padEnd(8) +
              gap.toFixed(1).padEnd(13) + pair);
}

const uniq = [...new Set(gaps)];
console.log('\ndistinct mark widths:', [...new Set(opts.map(b => (b.match(/<use[^>]*width="([^"]*)"/) || [])[1]))].join(', '));
console.log('distinct optical gaps:', uniq.join(', '));
console.log('mismatched pairs:', bad);
console.log('mojibake present:', /Â/.test(h));
if (uniq.length > 1) console.log('\nWARNING: gap is not constant across faces');