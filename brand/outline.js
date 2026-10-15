// Turn a string into SVG path data by reading the glyph outlines straight
// out of the TrueType `glyf` table. Produces a self-contained wordmark that
// needs no webfont at runtime.
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

// cmap format 4 -> glyph id
function glyphFor(buf, cmapT, code) {
  const base = cmapT.off;
  const n = buf.readUInt16BE(base + 2);
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
  if (buf.readUInt16BE(s) !== 4) return 0;
  const segX2 = buf.readUInt16BE(s + 6);
  const seg = segX2 / 2;
  const endO = s + 14, startO = endO + segX2 + 2, deltaO = startO + segX2, rangeO = deltaO + segX2;
  for (let i = 0; i < seg; i++) {
    const end = buf.readUInt16BE(endO + i * 2);
    if (code > end) continue;
    const start = buf.readUInt16BE(startO + i * 2);
    if (code < start) return 0;
    const delta = buf.readInt16BE(deltaO + i * 2);
    const ro = buf.readUInt16BE(rangeO + i * 2);
    if (ro === 0) return (code + delta) & 0xffff;
    const g = buf.readUInt16BE(rangeO + i * 2 + ro + (code - start) * 2);
    return g === 0 ? 0 : (g + delta) & 0xffff;
  }
  return 0;
}

function load(fontPath) {
  const buf = fs.readFileSync(fontPath);
  const t = tables(buf);
  const upem = buf.readUInt16BE(t.head.off + 18);
  const locaFmt = buf.readInt16BE(t.head.off + 50);
  const numGlyphs = buf.readUInt16BE(t.maxp.off + 4);
  const numH = buf.readUInt16BE(t.hhea.off + 34);

  // loca
  const loca = [];
  for (let i = 0; i <= numGlyphs; i++) {
    loca.push(locaFmt === 0 ? buf.readUInt16BE(t.loca.off + i * 2) * 2
                            : buf.readUInt32BE(t.loca.off + i * 4));
  }

  return {
    buf, upem, loca, t,
    cap: buf.readInt16BE(t['OS/2'].off + 88),
    advance(gid) {
      return gid < numH ? buf.readUInt16BE(t.hmtx.off + gid * 4) : 0;
    },
    lsb(gid) {
      return gid < numH ? buf.readInt16BE(t.hmtx.off + gid * 4 + 2)
                        : buf.readInt16BE(t.hmtx.off + numH * 4 + (gid - numH) * 2);
    },
    gid(code) { return glyphFor(buf, t.cmap, code); },
    contours(gid) {
      const s = loca[gid], e = loca[gid + 1];
      if (s === e) return [];
      let p = t.glyf.off + s;
      const n = buf.readInt16BE(p);
      if (n < 0) throw new Error('glyph ' + gid + ' is composite; not supported');
      p += 10; // numberOfContours + bbox
      const endPts = [];
      for (let i = 0; i < n; i++) { endPts.push(buf.readUInt16BE(p)); p += 2; }
      const numPts = endPts[n - 1] + 1;
      const instrLen = buf.readUInt16BE(p); p += 2 + instrLen;

      const flags = [];
      while (flags.length < numPts) {
        const f = buf.readUInt8(p++);
        flags.push(f);
        if (f & 8) { const r = buf.readUInt8(p++); for (let i = 0; i < r; i++) flags.push(f); }
      }
      const xs = []; let x = 0;
      for (let i = 0; i < numPts; i++) {
        const f = flags[i];
        if (f & 2) { const d = buf.readUInt8(p++); x += (f & 16) ? d : -d; }
        else if (!(f & 16)) { x += buf.readInt16BE(p); p += 2; }
        xs.push(x);
      }
      const ys = []; let y = 0;
      for (let i = 0; i < numPts; i++) {
        const f = flags[i];
        if (f & 4) { const d = buf.readUInt8(p++); y += (f & 32) ? d : -d; }
        else if (!(f & 32)) { y += buf.readInt16BE(p); p += 2; }
        ys.push(y);
      }
      const out = []; let s0 = 0;
      for (const last of endPts) {
        const pts = [];
        for (let i = s0; i <= last; i++) pts.push({ x: xs[i], y: ys[i], on: !!(flags[i] & 1) });
        out.push(pts); s0 = last + 1;
      }
      return out;
    }
  };
}

// One contour -> "M ... Q ... L ... Z" in SVG user units.
function contourPath(pts, k, ox, oy) {
  const n = pts.length;
  const P = p => [(p.x * k + ox), (oy - p.y * k)];
  const r = v => Math.round(v * 100) / 100;

  // Pick the starting point, inventing one if the contour opens off-curve.
  let start, rest;
  if (pts[0].on) { start = pts[0]; rest = pts.slice(1); }
  else if (pts[n - 1].on) { start = pts[n - 1]; rest = pts.slice(0, n - 1); }
  else {
    start = { x: (pts[0].x + pts[n - 1].x) / 2, y: (pts[0].y + pts[n - 1].y) / 2, on: true };
    rest = pts.slice();
  }

  const [sx, sy] = P(start);
  let d = `M${r(sx)} ${r(sy)}`;
  let i = 0;
  while (i < rest.length) {
    const p = rest[i];
    if (p.on) {
      const [x, y] = P(p);
      d += `L${r(x)} ${r(y)}`;
      i += 1;
    } else {
      const nx = rest[i + 1] || start;
      const [cx, cy] = P(p);
      if (nx.on) {
        const [x, y] = P(nx);
        d += `Q${r(cx)} ${r(cy)} ${r(x)} ${r(y)}`;
        i += 2;
      } else {
        const mid = { x: (p.x + nx.x) / 2, y: (p.y + nx.y) / 2 };
        const [mx, my] = P(mid);
        d += `Q${r(cx)} ${r(cy)} ${r(mx)} ${r(my)}`;
        i += 1;
      }
    }
  }
  return d + 'Z';
}

/**
 * Outline `text` and return path data plus the ink bounding box.
 * k scales font units to SVG units; origin (ox, oy) is the pen start / baseline.
 */
function outline(font, text, size, ox, oy) {
  const k = size / font.upem;
  let pen = ox;
  let d = '';
  let xMin = Infinity, yMin = Infinity, xMax = -Infinity, yMax = -Infinity;

  for (const ch of text) {
    const gid = font.gid(ch.codePointAt(0));
    const adv = font.advance(gid) * k;
    for (const pts of font.contours(gid)) {
      d += contourPath(pts, k, pen, oy);
      for (const p of pts) {
        const x = p.x * k + pen, y = oy - p.y * k;
        if (x < xMin) xMin = x;
        if (x > xMax) xMax = x;
        if (y < yMin) yMin = y;
        if (y > yMax) yMax = y;
      }
    }
    pen += adv;
  }
  return { d, advance: pen - ox, bbox: { xMin, yMin, xMax, yMax }, width: pen - ox };
}

module.exports = { load, outline };