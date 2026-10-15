// Font metrics + first-glyph side bearing, read straight from the binary
// tables. Runtime getBBox() proved unreliable here, so nothing in the
// layout path depends on the browser.
const fs = require('fs');

function tables(buf) {
  const num = buf.readUInt16BE(4);
  const map = {};
  for (let i = 0; i < num; i++) {
    const p = 12 + i * 16;
    map[buf.toString('latin1', p, p + 4).trim()] = {
      off: buf.readUInt32BE(p + 8),
      len: buf.readUInt32BE(p + 12)
    };
  }
  return map;
}

// cmap format 4 lookup -> glyph id
function glyphFor(buf, cmapT, code) {
  const base = cmapT.off;
  const n = buf.readUInt16BE(base + 2);
  let best = -1;
  for (let i = 0; i < n; i++) {
    const p = base + 4 + i * 8;
    const plat = buf.readUInt16BE(p);
    const enc = buf.readUInt16BE(p + 2);
    const off = buf.readUInt32BE(p + 4);
    const score = (plat === 3 && enc === 10) ? 3 : (plat === 3 && enc === 1) ? 2 : (plat === 0) ? 1 : 0;
    if (score > best) best = score;
  }
  // Re-walk to the winning subtable.
  let chosen = -1, bestScore = -1;
  for (let i = 0; i < n; i++) {
    const p = base + 4 + i * 8;
    const plat = buf.readUInt16BE(p), enc = buf.readUInt16BE(p + 2);
    const off = buf.readUInt32BE(p + 4);
    const score = (plat === 3 && enc === 10) ? 3 : (plat === 3 && enc === 1) ? 2 : (plat === 0) ? 1 : 0;
    if (score > bestScore) { bestScore = score; chosen = off; }
  }
  if (chosen < 0) return 0;
  const s = base + chosen;
  const format = buf.readUInt16BE(s);
  if (format !== 4) return 0;
  const segX2 = buf.readUInt16BE(s + 6);
  const seg = segX2 / 2;
  const endO = s + 14;
  const startO = endO + segX2 + 2;
  const deltaO = startO + segX2;
  const rangeO = deltaO + segX2;
  for (let i = 0; i < seg; i++) {
    const end = buf.readUInt16BE(endO + i * 2);
    if (code > end) continue;
    const start = buf.readUInt16BE(startO + i * 2);
    if (code < start) return 0;
    const delta = buf.readInt16BE(deltaO + i * 2);
    const ro = buf.readUInt16BE(rangeO + i * 2);
    if (ro === 0) return (code + delta) & 0xffff;
    const gi = rangeO + i * 2 + ro + (code - start) * 2;
    const g = buf.readUInt16BE(gi);
    return g === 0 ? 0 : (g + delta) & 0xffff;
  }
  return 0;
}

function fontData(file, firstChar) {
  const buf = fs.readFileSync(file);
  const t = tables(buf);
  const upem = buf.readUInt16BE(t.head.off + 18);
  const os2 = t['OS/2'], hhea = t.hhea, hmtx = t.hmtx;

  const code = firstChar.codePointAt(0);
  const gid = glyphFor(buf, t.cmap, code);
  const numH = buf.readUInt16BE(hhea.off + 34);
  const advance = buf.readUInt16BE(hmtx.off + gid * 4);
  const lsb = buf.readInt16BE(hmtx.off + gid * 4 + 2);

  return {
    upem,
    x: buf.readInt16BE(os2.off + 86) / upem,
    cap: buf.readInt16BE(os2.off + 88) / upem,
    asc: buf.readInt16BE(hhea.off + 4) / upem,
    lsb: lsb / upem,        // side bearing as a fraction of the em
    advance: advance / upem
  };
}

module.exports = { fontData };

if (require.main === module) {
  const path = require('path');
  const cfg = require('./set.js');
  console.log('name'.padEnd(14) + 'first'.padEnd(7) + 'gid'.padEnd(6) +
              'lsb/upem'.padEnd(10) + 'lsb@size'.padEnd(11) + 'adv/upem');
  console.log('-'.repeat(62));
  for (const o of cfg.options) {
    const d = fontData(path.join(__dirname, 'fonts', o.file), o.text[0]);
    const size = o.size + (o.matchBody ? 0 : cfg.SIZE_DELTA);
    console.log(o.name.padEnd(14) + o.text[0].padEnd(7) +
      String(Math.round(d.lsb * o.size)).padEnd(6) +
      d.lsb.toFixed(4).padEnd(10) +
      (d.lsb * size).toFixed(1).padEnd(11) +
      d.advance.toFixed(4));
  }
}