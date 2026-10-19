/**
 * "Convert to task": each line becomes a Markdown task line ("- [ ] …"), keeping its indent. List items
 * keep their text, headings and quotes lose their markers, lines that are already tasks or blank stay
 * as they are. Saving the note turns the new lines into real tasks, as typing them would.
 */
export function toTaskLines(text: string): { text: string; changed: number } {
  let changed = 0;
  const out = text.split('\n').map((line) => {
    if (!line.trim() || /^\s*(?:[-*+]|\d+[.)])\s+\[[ xX]\]\s/.test(line)) return line;
    const indent = /^\s*/.exec(line)![0];
    const rest = line.slice(indent.length)
      .replace(/^(?:[-*+]|\d+[.)])\s+/, '')
      .replace(/^#{1,6}\s+/, '')
      .replace(/^>\s?/, '')
      .trim();
    if (!rest) return line;
    changed++;
    return `${indent}- [ ] ${rest}`;
  });
  return { text: out.join('\n'), changed };
}
