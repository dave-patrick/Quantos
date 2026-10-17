import { Marked } from 'marked';
import DOMPurify from 'dompurify';
import { app } from './state.svelte';

const ULID = /^[0-9A-HJKMNP-TV-Z]{26}$/;

/** Markdown to sanitized HTML. Vault content is data: scripts and event handlers never survive. */
const marked = new Marked({ gfm: true, breaks: false });

/**
 * Footnotes (`[^1]` and `[^1]: text`): definitions are lifted out and listed at the end, references
 * become numbered superscripts. Code blocks and inline code are left alone.
 */
export function footnotes(body: string): { body: string; notes: { id: string; text: string }[] } {
  const defs = new Map<string, string>();
  const kept: string[] = [];
  let fence = false;
  let current: string | null = null;
  for (const line of body.split('\n')) {
    if (/^\s*(```|~~~)/.test(line)) { fence = !fence; current = null; kept.push(line); continue; }
    if (fence) { kept.push(line); continue; }
    const def = /^\[\^([^\]\s]+)\]:\s?(.*)$/.exec(line);
    if (def) { current = def[1]; defs.set(current, def[2]); continue; }
    if (current && /^( {2,}|\t)\S/.test(line)) { defs.set(current, defs.get(current) + '\n' + line.trim()); continue; }
    current = null;
    kept.push(line);
  }
  if (!defs.size) return { body, notes: [] };
  const order: string[] = [];
  fence = false;
  const out = kept.map((line) => {
    if (/^\s*(```|~~~)/.test(line)) { fence = !fence; return line; }
    if (fence) return line;
    // Split around inline code so references inside backticks stay literal.
    return line.split(/(`+[^`]*?`+)/).map((part, i) => i % 2 ? part : part.replace(/\[\^([^\]\s]+)\]/g, (m, id: string) => {
      if (!defs.has(id)) return m;
      if (!order.includes(id)) order.push(id);
      const n = order.indexOf(id) + 1;
      return `<sup class="footnote-ref"><a href="#fn-${escapeAttr(id)}" id="fnref-${escapeAttr(id)}">${n}</a></sup>`;
    })).join('');
  });
  return { body: out.join('\n'), notes: order.map((id) => ({ id, text: defs.get(id)! })) };
}

export function renderMarkdown(body: string, options: { links?: boolean } = {}): string {
  const { body: withRefs, notes } = footnotes(body);
  const list = notes.length
    ? `\n\n<section class="footnotes"><ol>${notes.map((n) => `<li id="fn-${escapeAttr(n.id)}">${marked.parseInline(n.text, { async: false }) as string}</li>`).join('')}</ol></section>\n`
    : '';
  const prepared = (withRefs + list)
    .replace(/<!--task:[0-9A-HJKMNP-TV-Z]{26}-->/g, '')
    .replace(/(!?)\[\[([^\[\]|#\n]+?)(#[^\[\]|\n]+)?(?:\|([^\[\]\n]+))?\]\]/g, (_m, bang, target: string, frag, display) => {
      const t = target.trim();
      const text = display ?? (ULID.test(t) ? app.titleIndex.get(t)?.title ?? 'Untitled' : t);
      const label = (bang ? '↳ ' : '') + text + (frag && !frag.startsWith('#^') ? ' › ' + frag.slice(1) : '');
      return options.links === false ? label : `<a class="wikilink" data-target="${escapeAttr(t)}" href="#">${escapeHtml(label)}</a>`;
    });
  const html = (marked.parse(prepared, { async: false }) as string)
    // Callouts: a quote that starts with [!type] gets a label instead of showing the marker.
    .replace(/<blockquote>\s*<p>\[!(\w+)\][+-]?\s*/g, '<blockquote class="callout"><p><span class="callout-label">$1</span> ');
  return DOMPurify.sanitize(html, { ADD_ATTR: ['data-target'], FORBID_TAGS: ['style', 'iframe', 'form', 'input', 'object', 'embed'] });
}

export function stripFrontmatter(content: string) {
  return content.replace(/^﻿?---\r?\n[\s\S]*?\r?\n(---|\.\.\.)\r?\n?/, '');
}

function escapeHtml(s: string) {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
}
function escapeAttr(s: string) { return escapeHtml(s); }

/** Copy as rich text (HTML + plain fallback) or as Markdown. */
export async function copyRich(body: string) {
  const html = renderMarkdown(body, { links: false });
  const plain = readable(body);
  if (navigator.clipboard && 'write' in navigator.clipboard && typeof ClipboardItem !== 'undefined') {
    await navigator.clipboard.write([new ClipboardItem({ 'text/html': new Blob([html], { type: 'text/html' }), 'text/plain': new Blob([plain], { type: 'text/plain' }) })]);
  } else {
    await navigator.clipboard.writeText(plain);
  }
}

/** Links with their readable titles and no hidden task markers, so text makes sense outside Quantos. */
export function readable(body: string) {
  return body
    .replace(/<!--task:[0-9A-HJKMNP-TV-Z]{26}-->/g, '')
    .replace(/\[\[([0-9A-HJKMNP-TV-Z]{26})(#[^\]|]+)?\|([^\]]+)\]\]/g, '[[$3$2]]')
    .replace(/(!?)\[\[([0-9A-HJKMNP-TV-Z]{26})(#[^\]|]+)?\]\]/g, (_m, bang: string, id: string, frag = '') => `${bang}[[${app.titleIndex.get(id)?.title ?? 'Untitled'}${frag}]]`);
}

export async function copyMarkdown(body: string) {
  await navigator.clipboard.writeText(readable(body));
}

export function outline(body: string): { level: number; text: string; line: number }[] {
  const out: { level: number; text: string; line: number }[] = [];
  let fence = false;
  body.split('\n').forEach((line, i) => {
    if (/^\s*(```|~~~)/.test(line)) fence = !fence;
    if (fence) return;
    const m = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(line);
    if (m) out.push({ level: m[1].length, text: m[2].replace(/\[\[[^\]|]*\|([^\]]+)\]\]/g, '$1'), line: i + 1 });
  });
  return out;
}
