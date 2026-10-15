// Shared icon preparation: strip the squircle background for transparency and
// fix the filter regions.
//
// The original was drawn for a dark squircle tile, so its blur regions were
// sized to hide inside that tile. Once the background is gone the glow lands
// on arbitrary grounds and the regions clip mid-falloff, which shows up as
// hard rectangular seams through the glow and a grey block on light grounds.
//
// Filter regions are objectBoundingBox percentages, so each element needs
// margin >= its blur's true reach (offset + ~3 sigma). Measured against the
// real geometry these were marginal to insufficient:
//   pillShadow       needs 13.5u, had 12.2u on the 61u-tall pills
//   frontCardShadow  needs  98u, had ~100u on the card group
//   cardShadow       needs  62u
//   ambientArcGlow   needs  84u (sigma 28)
//   softOrbGlow      needs  66u (sigma 22)
// Generous percentages scale with each element, so buffers stay bounded.
const fs = require('fs');

const REGIONS = {
  ambientArcGlow:  '-70% -70% 240% 240%',
  softOrbGlow:     '-80% -80% 260% 260%',
  cardShadow:      '-70% -70% 240% 240%',
  frontCardShadow: '-90% -90% 280% 280%',
  pillShadow:      '-70% -70% 240% 240%'
};

const BACKGROUND = /\s*<!-- 1\. MAIN BACKGROUND SQUIRCLE -->[\s\S]*?<g id="Background">[\s\S]*?<\/g>/;
const VIEWBOX = '120 120 1020 1020';   // frames the artwork, verified against its bbox

function loadIcon(file) {
  const raw = fs.readFileSync(file, 'utf8');

  const stripped = raw.replace(BACKGROUND, '');
  if (stripped === raw) throw new Error('background group not removed');

  let body = stripped.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');

  let widened = 0;
  body = body.replace(/<filter id="([^"]+)"[^>]*>/g, (full, id) => {
    const r = REGIONS[id];
    if (!r) return full;
    widened++;
    return `<filter id="${id}" x="${r.split(' ')[0]}" y="${r.split(' ')[1]}" ` +
           `width="${r.split(' ')[2]}" height="${r.split(' ')[3]}">`;
  });

  return {
    body,
    symbol: `<symbol id="mark" viewBox="${VIEWBOX}">${body}</symbol>`,
    widened,
    viewBox: VIEWBOX
  };
}

module.exports = { loadIcon, REGIONS, VIEWBOX };