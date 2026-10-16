import { Marked } from 'marked';
import DOMPurify from 'dompurify';
import { app } from './state.svelte';

const ULID = /^[0-9A-HJKMNP-TV-Z]{26}$/;

/** Markdown to sanitized HTML. Vault content is data: scripts and event handlers never survive. */
const marked = new Marked({ gfm: true, breaks: false });

export function renderMarkdown(body: string, options: { links?: boolean } = {}): string {
  const prepared = body
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
  const plain = body;
  if (navigator.clipboard && 'write' in navigator.clipboard && typeof ClipboardItem !== 'undefined') {
    await navigator.clipboard.write([new ClipboardItem({ 'text/html': new Blob([html], { type: 'text/html' }), 'text/plain': new Blob([plain], { type: 'text/plain' }) })]);
  } else {
    await navigator.clipboard.writeText(plain);
  }
}

export async function copyMarkdown(body: string) {
  // Links copy with their readable titles so they make sense outside Quantos.
  const readable = body
    .replace(/<!--task:[0-9A-HJKMNP-TV-Z]{26}-->/g, '')
    .replace(/\[\[([0-9A-HJKMNP-TV-Z]{26})(#[^\]|]+)?\|([^\]]+)\]\]/g, '[[$3$2]]');
  await navigator.clipboard.writeText(readable);
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
