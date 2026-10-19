/**
 * Whiteboards (Release 1.2). A whiteboard is a note (`type: canvas`) whose body holds one fenced
 * ```canvas block in the JSON Canvas 1.0 format (jsoncanvas.org), so it can be read by other tools.
 * Cards for notes keep the note's id (`node`) as well as its path, so they survive renames and moves.
 */
export type Side = 'top' | 'right' | 'bottom' | 'left';
export interface CanvasNode {
  id: string;
  type: 'text' | 'file' | 'link' | 'group';
  x: number; y: number; width: number; height: number;
  color?: string;
  text?: string;          // text cards (Markdown)
  file?: string;          // file cards: vault path
  node?: string;          // file cards: Quantos note id
  url?: string;           // link cards
  label?: string;         // groups
}
export interface CanvasEdge { id: string; fromNode: string; toNode: string; fromSide?: Side; toSide?: Side; toEnd?: 'arrow' | 'none'; label?: string; color?: string }
export interface Canvas { nodes: CanvasNode[]; edges: CanvasEdge[] }

const FENCE = /```canvas[ \t]*\r?\n([\s\S]*?)\r?\n```/;

export const COLORS: Record<string, string> = { '1': '#ff8a8a', '2': '#f4b35e', '3': '#f4e06a', '4': '#7fd8a6', '5': '#78d3e8', '6': '#b49cff' };

export function emptyCanvas(): Canvas { return { nodes: [], edges: [] }; }

export function parseCanvas(body: string): { canvas: Canvas; error: string | null } {
  const m = FENCE.exec(body);
  if (!m) return { canvas: emptyCanvas(), error: null };
  try {
    const data = JSON.parse(m[1]);
    const nodes = (Array.isArray(data.nodes) ? data.nodes : []).filter((n: CanvasNode) => n && typeof n.id === 'string' && ['text', 'file', 'link', 'group'].includes(n.type))
      .map((n: CanvasNode) => ({ ...n, x: +n.x || 0, y: +n.y || 0, width: Math.max(40, +n.width || 240), height: Math.max(30, +n.height || 120) }));
    const ids = new Set(nodes.map((n: CanvasNode) => n.id));
    const edges = (Array.isArray(data.edges) ? data.edges : []).filter((e: CanvasEdge) => e && ids.has(e.fromNode) && ids.has(e.toNode));
    return { canvas: { nodes, edges }, error: null };
  } catch (e) {
    return { canvas: emptyCanvas(), error: (e as Error).message };
  }
}

/** Writes the canvas back into the body, keeping any text around the block (a description, say). */
export function serializeCanvas(body: string, canvas: Canvas): string {
  const round = (n: CanvasNode) => ({ ...n, x: Math.round(n.x), y: Math.round(n.y), width: Math.round(n.width), height: Math.round(n.height) });
  const json = JSON.stringify({ nodes: canvas.nodes.map(round), edges: canvas.edges }, null, 1);
  const block = '```canvas\n' + json + '\n```';
  return FENCE.test(body) ? body.replace(FENCE, () => block) : (body.trim() ? body.trimEnd() + '\n\n' : '') + block + '\n';
}

export function newId() {
  return Array.from(crypto.getRandomValues(new Uint8Array(8)), (b) => b.toString(16).padStart(2, '0')).join('');
}

export interface Box { x: number; y: number; width: number; height: number }

/** The point on a box's side where an edge attaches. */
export function anchor(b: Box, side: Side): { x: number; y: number } {
  switch (side) {
    case 'top': return { x: b.x + b.width / 2, y: b.y };
    case 'bottom': return { x: b.x + b.width / 2, y: b.y + b.height };
    case 'left': return { x: b.x, y: b.y + b.height / 2 };
    default: return { x: b.x + b.width, y: b.y + b.height / 2 };
  }
}

/** The sides that face each other, for edges saved without sides. */
export function facing(a: Box, b: Box): [Side, Side] {
  const dx = b.x + b.width / 2 - (a.x + a.width / 2), dy = b.y + b.height / 2 - (a.y + a.height / 2);
  return Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? ['right', 'left'] : ['left', 'right']) : dy > 0 ? ['bottom', 'top'] : ['top', 'bottom'];
}

/** A smooth path between two anchors, leaving and entering perpendicular to their sides. */
export function edgePath(a: { x: number; y: number }, as: Side, b: { x: number; y: number }, bs: Side): string {
  const d = Math.max(40, Math.hypot(b.x - a.x, b.y - a.y) / 3);
  const out = (p: { x: number; y: number }, s: Side) => ({ x: p.x + (s === 'right' ? d : s === 'left' ? -d : 0), y: p.y + (s === 'bottom' ? d : s === 'top' ? -d : 0) });
  const c1 = out(a, as), c2 = out(b, bs);
  return `M${a.x},${a.y} C${c1.x},${c1.y} ${c2.x},${c2.y} ${b.x},${b.y}`;
}

/** The bounding box of some cards, for "fit to screen". */
export function bounds(nodes: Box[]): Box | null {
  if (!nodes.length) return null;
  const x = Math.min(...nodes.map((n) => n.x)), y = Math.min(...nodes.map((n) => n.y));
  const r = Math.max(...nodes.map((n) => n.x + n.width)), btm = Math.max(...nodes.map((n) => n.y + n.height));
  return { x, y, width: r - x, height: btm - y };
}

/** Cards inside a group move with it. */
export function contained(group: Box, n: Box) {
  return n.x >= group.x && n.y >= group.y && n.x + n.width <= group.x + group.width && n.y + n.height <= group.y + group.height;
}
