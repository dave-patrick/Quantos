/**
 * Presentation mode (Release 1.1): a note becomes slides. A `---` line with a blank line before it
 * starts a new slide; a note without any is split at its # and ## headings instead. Lines after a
 * `Note:` (or `Notes:`) line are speaker notes, shown only when asked for.
 */
export interface Slide { body: string; notes: string }

const FENCE = /^\s*(```|~~~)/;
const MARKER = /[ \t]*<!--task:[0-9A-HJKMNP-TV-Z]{26}-->/g;

export function toSlides(title: string, body: string): Slide[] {
  const lines = body.replace(/\r\n/g, '\n').replace(MARKER, '').split('\n');
  const inFence: boolean[] = [];
  let fence = false;
  for (const line of lines) { if (FENCE.test(line)) { inFence.push(true); fence = !fence; } else inFence.push(fence); }

  const rules = lines.map((l, i) => !inFence[i] && /^\s*---\s*$/.test(l) && (i === 0 || lines[i - 1].trim() === ''));
  const chunks: string[][] = [[]];
  if (rules.some(Boolean)) {
    lines.forEach((l, i) => { if (rules[i]) chunks.push([]); else chunks[chunks.length - 1].push(l); });
  } else {
    lines.forEach((l, i) => {
      if (!inFence[i] && /^#{1,2}\s/.test(l) && chunks[chunks.length - 1].some((x) => x.trim())) chunks.push([]);
      chunks[chunks.length - 1].push(l);
    });
  }

  const slides = chunks
    .map((c) => {
      const at = c.findIndex((l) => /^\s*notes?:\s*$/i.test(l));
      const main = at >= 0 ? c.slice(0, at) : c;
      return { body: main.join('\n').trim(), notes: at >= 0 ? c.slice(at + 1).join('\n').trim() : '' };
    })
    .filter((s) => s.body || s.notes);

  // A title slide opens the deck unless the first slide already starts with a top heading.
  if (!slides.length || !/^#\s/.test(slides[0].body)) slides.unshift({ body: `# ${title.replace(/^#+\s*/, '')}`, notes: '' });
  return slides;
}
