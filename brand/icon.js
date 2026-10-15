// Shared icon preparation: strip the squircle background for transparency,
// widen the glow's filter regions, and frame the result so nothing is clipped.
//
// Two separate clipping problems had to be fixed, and they are easy to confuse:
//
// 1. Filter regions were too tight, so each blur was cut mid-falloff. Those
//    regions are objectBoundingBox percentages, so each needs margin at least
//    as large as its blur's reach (offset + ~3 sigma). Measured against the
//    real geometry they were marginal to insufficient:
//      pillShadow       needs 13.5u, had 12.2u on the 61u-tall pills
//      frontCardShadow  needs  98u, had ~100u on the card group
//      cardShadow       needs  62u
//      ambientArcGlow   needs  84u (sigma 28)
//      softOrbGlow      needs  66u (sigma 22)
//    Widening these is necessary but not sufficient - it makes the glow reach
//    further, which exposed problem 2.
//
// 2. The viewBox was framed on the ink alone (219..1042 / 219..1083), but the
//    glow renders well past it. Measured by scanning rendered pixels, the
//    visible extent runs 85..1165 / 86..1166 - so the glow was being sliced on
//    all four sides, which is what produced the hard straight edges.
//
// getBBox cannot measure either problem: it reports geometry only and never
// sees a filter. So the frame below is taken from a pixel scan.
const fs = require('fs');

const REGIONS = {
  ambientArcGlow:  '-70% -70% 240% 240%',
  softOrbGlow:     '-80% -80% 260% 260%',
  cardShadow:      '-70% -70% 240% 240%',
  frontCardShadow: '-90% -90% 280% 280%',
  pillShadow:      '-70% -70% 240% 240%'
};

const BACKGROUND = /\s*<!-- 1\. MAIN BACKGROUND SQUIRCLE -->[\s\S]*?<g id="Background">[\s\S]*?<\/g>/;

// True rendered extent, from a pixel scan at alpha > 8. The alpha 1..7 fringe
// outside this is far below anything perceptible; clipping it is invisible,
// while clipping anything stronger is not.
const FRAME_GLOW = { x: 104, y: 103, w: 1045, h: 1046 };

// Ink is what actually reads as the letterform. Measured by pixel scan, NOT by
// getBBox: getBBox reports geometry only and excludes stroke, and the arcs
// carry stroke-width 128, so getBBox understated the mark by 64 units a side
// (it reported 219..1042 against a true 155..1097). Sizing off that made the Q
// render at ~131u instead of 120u and pulled the real gap in to ~10u.
const INK = { x: 155, y: 155, w: 942, h: 942 };

// Flat has no halo, so the frame is the ink itself and the box comes out at
// exactly MARK - which is the cleanest possible relationship for a wordmark.
const frameFor = flat => (flat ? INK : FRAME_GLOW);

function loadIcon(file, opts) {
  const flat = !!(opts && opts.flat);
  const raw = fs.readFileSync(file, 'utf8');

  const stripped = raw.replace(BACKGROUND, '');
  if (stripped === raw) throw new Error('background group not removed');

  let body = stripped.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');

  let widened = 0, removed = 0;
  if (flat) {
    // Drop the 3D treatment outright: no glow, no drop shadows. At wordmark
    // size these contribute a faint halo and a grey block, nothing else.
    body = body.replace(/\s*<filter id="[^"]*"[\s\S]*?<\/filter>/g, () => { removed++; return ''; });
    body = body.replace(/\s*filter="url\(#[^)]*\)"/g, '');
  } else {
    body = body.replace(/<filter id="([^"]+)"[^>]*>/g, (full, id) => {
      const r = REGIONS[id];
      if (!r) return full;
      widened++;
      const [x, y, w, h] = r.split(' ');
      return `<filter id="${id}" x="${x}" y="${y}" width="${w}" height="${h}">`;
    });
  }

  const viewBox = `${FRAME_GLOW.x} ${FRAME_GLOW.y} ${FRAME_GLOW.w} ${FRAME_GLOW.h}`;
  return {
    body, flat,
    symbol: `<symbol id="mark" viewBox="${flat ? `${INK.x} ${INK.y} ${INK.w} ${INK.h}` : viewBox}">${body}</symbol>`,
    widened, removed,
    viewBox: flat ? `${INK.x} ${INK.y} ${INK.w} ${INK.h}` : viewBox,
    frame: frameFor(flat), ink: INK
  };
}

module.exports = { loadIcon, REGIONS, FRAME_GLOW, INK, frameFor };