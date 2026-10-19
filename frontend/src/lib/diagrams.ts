/**
 * Math ($…$, $$…$$) and Mermaid diagrams (Release 1.1). Both libraries are bundled, so they work offline,
 * and are only loaded the first time a note uses them.
 */
type Katex = typeof import('katex').default;
type Mermaid = typeof import('mermaid').default;

let katexLoad: Promise<Katex> | null = null;
let mermaidLoad: Promise<Mermaid> | null = null;
let mermaidTheme = '';
let seq = 0;

function katex() {
  return (katexLoad ??= Promise.all([import('katex'), import('katex/dist/katex.min.css')]).then(([k]) => k.default));
}

async function mermaid() {
  const m = await (mermaidLoad ??= import('mermaid').then((x) => x.default));
  // Diagrams take the app's own colours (and the accent the person picked), so they match light and dark.
  const css = getComputedStyle(document.documentElement);
  const v = (name: string) => css.getPropertyValue(name).trim();
  const vars = {
    darkMode: document.documentElement.dataset.theme !== 'light',
    background: v('--bg'), primaryColor: v('--bg-panel'), primaryBorderColor: v('--accent'), primaryTextColor: v('--text'),
    secondaryColor: v('--accent-wash'), tertiaryColor: v('--bg-raised'), lineColor: v('--text-muted'), textColor: v('--text'),
    edgeLabelBackground: v('--bg-raised'), clusterBkg: v('--bg-raised'), clusterBorder: v('--line-strong'),
    noteBkgColor: v('--warn-wash'), noteTextColor: v('--text'), noteBorderColor: v('--warn'),
    fontFamily: v('--font-body'), fontSize: '15px'
  };
  const key = JSON.stringify(vars);
  if (key !== mermaidTheme) {
    m.initialize({ startOnLoad: false, securityLevel: 'strict', theme: 'base', look: 'classic', themeVariables: vars });
    mermaidTheme = key;
  }
  return m;
}

const mathCache = new Map<string, string>();

export async function renderMath(tex: string, display: boolean): Promise<string> {
  const key = (display ? 'D' : 'I') + tex;
  const hit = mathCache.get(key);
  if (hit) return hit;
  const k = await katex();
  const html = k.renderToString(tex, { displayMode: display, throwOnError: false, output: 'htmlAndMathml', trust: false, strict: 'ignore' });
  if (mathCache.size > 500) mathCache.clear();
  mathCache.set(key, html);
  return html;
}

const diagramCache = new Map<string, string>();

/** Renders Mermaid source to SVG; a mistake in the source becomes a readable message instead. */
export async function renderDiagram(source: string): Promise<{ svg: string } | { error: string }> {
  const m = await mermaid();
  const key = mermaidTheme + '\n' + source;
  const hit = diagramCache.get(key);
  if (hit) return { svg: hit };
  try {
    const { svg } = await m.render('q-mermaid-' + ++seq, source);
    if (diagramCache.size > 100) diagramCache.clear();
    diagramCache.set(key, svg);
    return { svg };
  } catch (e) {
    document.getElementById('dq-mermaid-' + seq)?.remove();
    return { error: (e instanceof Error ? e.message : String(e)).split('\n').slice(0, 4).join('\n') };
  }
}

/** Fills an element with a diagram, or with the error and the source when it can't be drawn. */
export function drawDiagram(el: HTMLElement, source: string) {
  el.classList.add('q-diagram');
  el.textContent = '';
  void renderDiagram(source).then((r) => {
    if ('svg' in r) { el.innerHTML = r.svg; el.removeAttribute('data-error'); }
    else {
      el.dataset.error = '1';
      const msg = document.createElement('div');
      msg.className = 'q-diagram-error';
      msg.textContent = 'This diagram has a mistake: ' + r.error;
      const pre = document.createElement('pre');
      pre.textContent = source;
      el.replaceChildren(msg, pre);
    }
  });
}

export function drawMath(el: HTMLElement, tex: string, display: boolean) {
  el.classList.add('q-math');
  if (display) el.classList.add('display');
  const cached = mathCache.get((display ? 'D' : 'I') + tex);
  if (cached) { el.innerHTML = cached; return; }
  el.textContent = display ? `$$${tex}$$` : `$${tex}$`;
  void renderMath(tex, display).then((html) => { el.innerHTML = html; });
}

/**
 * Finds math and diagram placeholders left by renderMarkdown (which is synchronous) and draws them.
 * Runs once for the whole document so every preview, hover card and answer gets them.
 */
export function watchRichContent(root: HTMLElement = document.body) {
  const scan = (node: ParentNode) => {
    node.querySelectorAll<HTMLElement>('[data-q-tex]:not([data-q-done])').forEach((el) => {
      el.dataset.qDone = '1';
      drawMath(el, el.dataset.qTex ?? '', el.dataset.qDisplay === '1');
    });
    node.querySelectorAll<HTMLElement>('pre > code.language-mermaid').forEach((code) => {
      const pre = code.parentElement!;
      const div = document.createElement('div');
      pre.replaceWith(div);
      drawDiagram(div, code.textContent ?? '');
    });
  };
  scan(root);
  new MutationObserver((list) => {
    for (const m of list) for (const n of m.addedNodes) if (n instanceof HTMLElement) scan(n.parentElement ?? n);
  }).observe(root, { childList: true, subtree: true });
}

/**
 * Finds math in Markdown text outside code: $$…$$ (may span lines) and $…$ (Pandoc's rule: no space just
 * inside the dollars and no digit right after the closing one, so "$5 and $10" stays text).
 */
export function findMath(text: string): { from: number; to: number; tex: string; display: boolean }[] {
  const out: { from: number; to: number; tex: string; display: boolean }[] = [];
  const code = codeRanges(text);
  const inCode = (i: number) => code.some(([a, b]) => i >= a && i < b);
  const re = /\$\$([\s\S]+?)\$\$|(?<![\\$\w])\$(?![\s$])((?:\\\$|[^$\n])+?)(?<![\s\\])\$(?![\d$])/g;
  for (let m: RegExpExecArray | null; (m = re.exec(text));) {
    if (inCode(m.index)) continue;
    const display = m[1] !== undefined;
    const tex = (display ? m[1] : m[2]).trim();
    if (tex) out.push({ from: m.index, to: m.index + m[0].length, tex, display });
  }
  return out;
}

/** Fenced code blocks and inline code spans, where dollars are literal. */
function codeRanges(text: string): [number, number][] {
  const ranges: [number, number][] = [];
  const fence = /^[ \t]*(```|~~~)[^\n]*\n[\s\S]*?(?:^[ \t]*\1[ \t]*$|(?![\s\S]))/gm;
  for (let m: RegExpExecArray | null; (m = fence.exec(text));) ranges.push([m.index, m.index + m[0].length]);
  const inline = /(`+)[^`]*?\1/g;
  for (let m: RegExpExecArray | null; (m = inline.exec(text));) {
    if (!ranges.some(([a, b]) => m!.index >= a && m!.index < b)) ranges.push([m.index, m.index + m[0].length]);
  }
  return ranges;
}
