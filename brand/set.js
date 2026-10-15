// Single source of truth for the six letterings. Edit here, run
// `node render.js && node build-preview.js`, and every size, stroke,
// Q position and label follows automatically.
module.exports = {
  MARK: 120,        // Q is MARK x MARK units on every option
  GAP: 18,          // optical gap: icon ink -> first glyph ink (not box edges)
  BASE: 200,        // text baseline y
  FRAME_H: 220,     // viewBox height used to frame the wordmark
  DARK_FACTOR: 0.88,// light strokes on a dark ground bloom

  // Applied to every face at once, in the SVG's own user units.
  SIZE_DELTA: -10,

  options: [
    {
      name: 'Gruppo', file: 'Gruppo.ttf', css: 'Gruppo, sans-serif',
      text: 'uantos', caps: false, size: 165, stroke: 6.0,
      tag: 'locked', locked: true,
      blurb: 'Bold condensed on a grid skeleton. Dense, confident, reads engineered.'
    },
    {
      name: 'Anta', file: 'Anta.ttf', css: 'Anta, sans-serif',
      text: 'uantos', caps: false, size: 131, stroke: 2.5,
      blurb: 'Heavy squared display with clipped corners. Graphic and confident.'
    },
    {
      name: 'Baumans', file: 'Baumans.ttf', css: 'Baumans, sans-serif',
      text: 'uantos', caps: false, size: 136, stroke: 3.3,
      tag: 'keeping',
      blurb: 'Squared techno. Heavier than Anta, lighter than Gruppo.'
    },
    {
      name: 'Turret Road', file: 'Turret_Road.ttf', css: "'Turret Road', sans-serif",
      text: 'uantos', caps: false, size: 146, stroke: 3.5,
      tag: 'keeping',
      blurb: 'Squared and mechanical. Sci-fi without costume.'
    },
    {
      name: 'Zen Dots', file: 'Zen_Dots.ttf', css: "'Zen Dots', sans-serif",
      text: 'uantos', caps: false, size: 118, stroke: 1.6,
      tag: 'lighter',
      blurb: 'Dot-constructed, trimmed back — the dots carry more visual weight than their metrics suggest.'
    },
    {
      name: 'Bruno Ace SC', file: 'Bruno_Ace_SC.ttf', css: "'Bruno Ace SC', sans-serif",
      text: 'UANTOS', caps: true, size: 118, stroke: 0,
      matchBody: true,   // sized so its cap band equals the set's mean body band
      tag: 'lighter',
      blurb: 'Condensed engineered caps. Auto-sized to match its cap band against the lowercase x-heights, so it reads at the same visual size as the rest.'
    }
  ]
};