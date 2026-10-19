/**
 * Mind maps with annotations (Release 1.2). A note's headings and list items become branches; an
 * annotation on a branch is a Markdown footnote on that line ("text [^m1]" plus "[^m1]: note" at the
 * end), so it stays readable in the note itself and in any other Markdown tool.
 */
export interface Annotation { id: string; text: string }
export interface Branch { key: string; text: string; line: number; depth: number; kind: 'root' | 'heading' | 'item'; done?: boolean; notes: Annotation[]; children: Branch[] }

const FENCE = /^\s*(```|~~~)/;
const DEF = /^\[\^([^\]\s]+)\]:\s?(.*)$/;
const REF = /\[\^([^\]\s]+)\](?!:)/g;
const MARKER = /[ \t]*<!--task:[0-9A-HJKMNP-TV-Z]{26}-->/;

/** Footnote definitions (with indented continuation lines), by id. */
export function definitions(lines: string[]): Map<string, { text: string; from: number; to: number }> {
  const defs = new Map<string, { text: string; from: number; to: number }>();
  let fence = false;
  for (let i = 0; i < lines.length; i++) {
    if (FENCE.test(lines[i])) { fence = !fence; continue; }
    if (fence) continue;
    const m = DEF.exec(lines[i]);
    if (!m) continue;
    let to = i, text = m[2];
    while (to + 1 < lines.length && /^( {2,}|\t)\S/.test(lines[to + 1])) { to++; text += '\n' + lines[to].trim(); }
    defs.set(m[1], { text, from: i, to });
  }
  return defs;
}

/** Plain words for a branch: links by their text, no Markdown marks, no footnote references. */
export function plain(s: string) {
  return s.replace(MARKER, '').replace(REF, '')
    .replace(/!?\[\[[0-9A-HJKMNP-TV-Z]{26}\|([^\]]+)\]\]/g, '$1').replace(/!?\[\[([^\]|]+)(?:\|([^\]]+))?\]\]/g, (_m, a, b) => b ?? a)
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, '$1').replace(/(\*\*|__|\*|_|~~|`)/g, '').trim();
}

export function outline(title: string, body: string): Branch {
  const lines = body.replace(/\r\n/g, '\n').split('\n');
  const defs = definitions(lines);
  const root: Branch = { key: 'root', text: title, line: -1, depth: 0, kind: 'root', notes: [], children: [] };
  const headings: Branch[] = [root];          // stack by heading level
  let items: { indent: number; branch: Branch }[] = [];
  let fence = false;
  const notesOf = (line: string) => [...line.matchAll(REF)].map((m) => m[1]).filter((id, i, all) => defs.has(id) && all.indexOf(id) === i).map((id) => ({ id, text: defs.get(id)!.text }));

  lines.forEach((line, n) => {
    if (FENCE.test(line)) { fence = !fence; return; }
    if (fence || DEF.test(line)) return;
    const h = /^(#{1,6})\s+(.*)$/.exec(line);
    if (h) {
      const level = h[1].length;
      while (headings.length > 1 && headings[headings.length - 1].depth >= level) headings.pop();
      const branch: Branch = { key: 'l' + n, text: plain(h[2]), line: n, depth: level, kind: 'heading', notes: notesOf(line), children: [] };
      headings[headings.length - 1].children.push(branch);
      headings.push(branch);
      items = [];
      return;
    }
    const li = /^(\s*)(?:[-*+]|\d+[.)])\s+(?:\[([ xX])\]\s+)?(.*)$/.exec(line);
    if (li && li[3].trim()) {
      const indent = li[1].replace(/\t/g, '  ').length;
      while (items.length && items[items.length - 1].indent >= indent) items.pop();
      const parent = items.length ? items[items.length - 1].branch : headings[headings.length - 1];
      const branch: Branch = { key: 'l' + n, text: plain(li[3]), line: n, depth: parent.depth + 1, kind: 'item', done: li[2] ? li[2] !== ' ' : undefined, notes: notesOf(line), children: [] };
      parent.children.push(branch);
      items.push({ indent, branch });
    } else if (line.trim() && !/^\s/.test(line)) items = [];
  });
  return root;
}

function nextId(lines: string[]) {
  const used = new Set([...definitions(lines).keys()]);
  let i = 1;
  while (used.has('m' + i)) i++;
  return 'm' + i;
}

/** Adds an annotation to a line: a reference at its end (before a task marker) and the text at the end of the note. */
export function annotate(body: string, line: number, text: string): string {
  const lines = body.replace(/\r\n/g, '\n').split('\n');
  if (line < 0 || line >= lines.length || !text.trim()) return body;
  const id = nextId(lines);
  const marker = MARKER.exec(lines[line]);
  lines[line] = marker ? lines[line].slice(0, marker.index) + ` [^${id}]` + lines[line].slice(marker.index) : lines[line].replace(/\s*$/, '') + ` [^${id}]`;
  while (lines.length && lines[lines.length - 1] === '') lines.pop();
  const hasDefs = lines.length && DEF.test(lines[lines.length - 1]);
  lines.push(...(hasDefs ? [] : ['']), `[^${id}]: ${oneLine(text)}`, '');
  return lines.join('\n');
}

export function editAnnotation(body: string, id: string, text: string): string {
  const lines = body.replace(/\r\n/g, '\n').split('\n');
  const def = definitions(lines).get(id);
  if (!def) return body;
  lines.splice(def.from, def.to - def.from + 1, `[^${id}]: ${oneLine(text)}`);
  return lines.join('\n');
}

export function removeAnnotation(body: string, id: string): string {
  const lines = body.replace(/\r\n/g, '\n').split('\n');
  const def = definitions(lines).get(id);
  if (def) lines.splice(def.from, def.to - def.from + 1);
  const ref = new RegExp(`[ \\t]?\\[\\^${id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\](?!:)`, 'g');
  return lines.map((l) => l.replace(ref, '')).join('\n').replace(/\n{3,}$/, '\n\n');
}

/** Footnote text is one line; line breaks in an annotation become an indented continuation. */
const oneLine = (t: string) => t.trim().split(/\r?\n/).map((l, i) => (i ? '    ' + l.trim() : l.trim())).join('\n');
