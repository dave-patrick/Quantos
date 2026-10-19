import { setActiveEditor } from '../activeEditor';
import { StateField, type EditorState, type Extension, type Range } from '@codemirror/state';
import { Decoration, EditorView, WidgetType, type DecorationSet } from '@codemirror/view';
import { app } from '../state.svelte';
import { layout } from '../layout.svelte';

/** Upload a file to the vault's attachments (stored once by content) and return Markdown that references it. */
export async function uploadAttachment(file: File): Promise<string> {
  const res = await fetch(`/api/attachments?name=${encodeURIComponent(file.name || 'pasted.png')}`, {
    method: 'POST', credentials: 'same-origin', headers: { 'X-Quantos-Request': '1', 'Content-Type': 'application/octet-stream' }, body: file
  });
  const data = await res.json().catch(() => null);
  if (!res.ok) throw new Error(data?.error ?? 'Upload failed');
  return data.markdown as string;
}

async function insertFiles(view: EditorView, files: File[], pos: number) {
  for (const file of files) {
    try {
      const md = await uploadAttachment(file);
      view.dispatch({ changes: { from: pos, insert: md + '\n' } });
      pos += md.length + 1;
    } catch (e) { app.error(e); }
  }
}

export function attachmentDrop(hooks: MediaHooks = {}): Extension {
  const mediaHooks = hooks;
  return EditorView.domEventHandlers({
  paste(event, view) {
    const files = [...(event.clipboardData?.files ?? [])];
    // A web address pasted over selected words makes them a link.
    const text = event.clipboardData?.getData('text/plain')?.trim() ?? '';
    const sel = view.state.selection.main;
    if (!files.length && !sel.empty && /^https?:\/\/\S+$/i.test(text)) {
      const label = view.state.sliceDoc(sel.from, sel.to);
      if (!label.includes('\n') && !/[[\]]/.test(label)) {
        event.preventDefault();
        const insert = `[${label}](${text})`;
        view.dispatch({ changes: { from: sel.from, to: sel.to, insert }, selection: { anchor: sel.from + insert.length }, userEvent: 'input.paste' });
        return true;
      }
    }
    if (!files.length) return false;
    event.preventDefault();
    insertFiles(view, files, view.state.selection.main.head);
    return true;
  },
  drop(event, view) {
    const files = [...(event.dataTransfer?.files ?? [])];
    if (!files.length) return false;
    event.preventDefault();
    const pos = view.posAtCoords({ x: event.clientX, y: event.clientY }) ?? view.state.selection.main.head;
    insertFiles(view, files, pos);
    return true;
  },
  mousedown(event, view) {
    const web = (event.target as HTMLElement).closest('.cm-web-link') as HTMLElement | null;
    if (web?.dataset.href && /^(https?:\/\/|mailto:)/i.test(web.dataset.href)) {
      // Opens in the browser (the Windows client hands new windows to the default browser).
      event.preventDefault();
      window.open(web.dataset.href, '_blank', 'noopener');
      return true;
    }
    // A link to a note next door, as another app writes them.
    const near = (event.target as HTMLElement).closest('[data-note]') as HTMLElement | null;
    if (near?.dataset.note) {
      event.preventDefault();
      mediaHooks.openLink?.(near.dataset.note, event);
      return true;
    }
    const el = (event.target as HTMLElement).closest('[data-pdf]') as HTMLElement | null;
    if (!el) return false;
    event.preventDefault();
    // This note becomes the one "Cite this page" writes into, just after the PDF's link.
    const line = view.state.doc.lineAt(view.posAtDOM(el));
    view.dispatch({ selection: { anchor: line.to } });
    setActiveEditor(view);
    layout.open({ kind: 'pdf', arg: el.dataset.pdf!, title: el.dataset.name ?? 'PDF' }, event.shiftKey ? 'other' : 'new');
    return true;
  }
  });
}

class ImageWidget extends WidgetType {
  constructor(readonly src: string, readonly alt: string) { super(); }
  eq(o: ImageWidget) { return o.src === this.src; }
  toDOM() {
    const img = document.createElement('img');
    img.src = '/api/files/' + this.src.replace(/^attachments\//, 'attachments/');
    img.alt = this.alt;
    img.className = 'cm-inline-image';
    img.loading = 'lazy';
    return img;
  }
}

/** Recordings and other audio play in place, with a speed control for listening back quickly. */
class AudioWidget extends WidgetType {
  constructor(readonly src: string, readonly name: string) { super(); }
  eq(o: AudioWidget) { return o.src === this.src; }
  toDOM() {
    const box = document.createElement('div');
    box.className = 'cm-audio';
    const audio = document.createElement('audio');
    audio.controls = true;
    audio.preload = 'metadata';
    audio.src = '/api/files/' + this.src;
    audio.setAttribute('aria-label', this.name || 'Recording');
    // Recordings made in a browser carry no length; seeking to the end once makes the player find it.
    audio.addEventListener('loadedmetadata', () => {
      if (audio.duration !== Infinity && !Number.isNaN(audio.duration)) return;
      audio.addEventListener('timeupdate', () => { audio.currentTime = 0; }, { once: true });
      audio.currentTime = 1e9;
    }, { once: true });
    const speed = document.createElement('button');
    speed.className = 'cm-audio-speed';
    speed.textContent = '1×';
    speed.title = 'Playback speed';
    const speeds = [1, 1.25, 1.5, 2, 0.75];
    speed.onclick = () => {
      const next = speeds[(speeds.indexOf(audio.playbackRate) + 1) % speeds.length];
      audio.playbackRate = next;
      speed.textContent = next + '×';
    };
    box.append(audio, speed);
    return box;
  }
  ignoreEvent() { return true; }
  get estimatedHeight() { return 48; }
}

class PdfChip extends WidgetType {
  constructor(readonly path: string, readonly name: string, readonly page: string | null) { super(); }
  eq(o: PdfChip) { return o.path === this.path && o.page === this.page; }
  toDOM() {
    const s = document.createElement('span');
    s.className = 'cm-pdf-chip';
    s.dataset.pdf = this.path + (this.page ? '#page=' + this.page : '');
    s.dataset.name = this.name;
    s.textContent = '📄 Open PDF' + (this.page ? ' at page ' + this.page : '');
    return s;
  }
  ignoreEvent() { return false; }
}

const IMAGE_EXT = /\.(?:png|jpe?g|gif|webp|svg)$/i;
const AUDIO_EXT = /\.(?:mp3|m4a|wav|webm|ogg|opus)$/i;
const PDF_EXT = /\.pdf$/i;

// Files stored in the vault's attachments folder.
const IMAGE = /!\[([^\]]*)\]\((attachments\/[^)\s]+\.(?:png|jpe?g|gif|webp|svg))\)/i;
const AUDIO = /\[([^\]]*)\]\((attachments\/[^)\s]+\.(?:mp3|m4a|wav|webm|ogg|opus))\)/gi;
const PDF = /\[([^\]]*)\]\((attachments\/[^)\s#]+\.pdf)(?:#page=(\d+))?\)/gi;

/** Any Markdown link or image, whatever the target: the kind is decided from the path. */
const RELATIVE_LINK = /(!?)\[([^\]]*)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;
// Links to stored files: [name](attachments/ab/abcdef….ext), not images.
const ATTACHMENT_LINK = /(?<!!)\[([^\]]*)\]\((attachments\/[^)\s]+)\)/g;
const WEB_LINK = /(?<!!)\[([^\]]+)\]\(((?:https?:\/\/|mailto:)[^)\s]+)\)/g;
const hide = Decoration.replace({});
const fileName = Decoration.mark({ class: 'cm-attach-name' });

/**
 * A path that points at a file on disk, as opposed to a web address. Attachments live in the
 * vault's own folder; anything else is relative to the note it sits in, which is how another
 * app's notes are written.
 */
function isLocalPath(path: string): boolean {
  if (!path) return false;
  if (/^[a-z][a-z0-9+.-]*:/i.test(path)) return false; // http:, mailto:, file:
  if (path.startsWith('#') || path.startsWith('/')) return false;
  return true;
}

/**
 * The path a relative link points at, in the vault's own path space: a link in a note in a
 * linked folder resolves inside that folder, as the other app means it.
 */
export function resolveLocalPath(raw: string, notePath: string | null | undefined): string | null {
  if (!isLocalPath(raw)) return null;
  if (raw.startsWith('attachments/')) return raw;
  if (!notePath) return null;
  const dir = notePath.includes('/') ? notePath.slice(0, notePath.lastIndexOf('/')) : '';
  const parts = (dir ? dir.split('/') : []).concat(raw.split('/'));
  const out: string[] = [];
  for (const part of parts) {
    if (part === '.' || part === '') continue;
    if (part === '..') { out.pop(); continue; }
    out.push(part);
  }
  return out.join('/');
}

/**
 * Images show below their line; PDF links get an Open chip. Away from the cursor's line, the storage
 * path is hidden (a link reads as its name, an image as the picture); on that line the Markdown
 * is shown as written, so it can be edited.
 */
function build(state: EditorState, hooks: MediaHooks): DecorationSet {
  const decos: Range<Decoration>[] = [];
  const cursorLine = state.doc.lineAt(state.selection.main.head).number;
  const notePath = hooks.notePath?.() ?? null;
  for (let n = 1; n <= state.doc.lines; n++) {
    const line = state.doc.line(n);
    const raw = n === cursorLine;
    const img = IMAGE.exec(line.text);
    if (img) {
      if (!raw) decos.push(hide.range(line.from + img.index, line.from + img.index + img[0].length));
      decos.push(Decoration.widget({ widget: new ImageWidget(img[2], img[1]), side: 1, block: true }).range(line.to));
      continue;
    }
    for (const m of line.text.matchAll(ATTACHMENT_LINK)) {
      const start = line.from + m.index!, end = start + m[0].length, labelEnd = start + 1 + m[1].length;
      if (!raw && m[1].length) {
        decos.push(hide.range(start, start + 1), fileName.range(start + 1, labelEnd), hide.range(labelEnd, end));
      }
    }
    // Files that sit next to the note, as another app writes them: a picture, a recording, a PDF
    // or a link to the note in the same folder. They resolve against the note's own folder.
    for (const m of line.text.matchAll(RELATIVE_LINK)) {
      const target = m[3];
      if (!target || target.startsWith('attachments/') || !isLocalPath(target)) continue;
      const resolved = resolveLocalPath(target, notePath);
      if (!resolved) continue;
      const start = line.from + m.index!, end = start + m[0].length;
      const labelStart = start + 1, labelEnd = labelStart + m[2].length;
      if (m[1] === '!') {
        if (!IMAGE_EXT.test(resolved)) continue;
        if (!raw) decos.push(hide.range(start, end));
        decos.push(Decoration.widget({ widget: new ImageWidget(resolved, m[2]), side: 1, block: true }).range(line.to));
        continue;
      }
      if (AUDIO_EXT.test(resolved)) {
        decos.push(Decoration.widget({ widget: new AudioWidget(resolved, m[2]), side: 1, block: true }).range(line.to));
        continue;
      }
      if (PDF_EXT.test(resolved)) {
        decos.push(Decoration.widget({ widget: new PdfChip(resolved, m[2], null), side: 1 }).range(end));
        continue;
      }
      const note = resolved.toLowerCase().endsWith('.md') ? hooks.findByPath?.(resolved) : null;
      if (!raw && m[2].length) {
        decos.push(hide.range(start, labelStart), fileName.range(labelStart, labelEnd), hide.range(labelEnd, end));
      }
      if (note) decos.push(Decoration.mark({ class: 'cm-attach-name', attributes: { 'data-note': note.id, title: note.title } }).range(labelStart, labelEnd));
    }
    // Web and mail links read as their text; clicking the text opens them.
    for (const m of line.text.matchAll(WEB_LINK)) {
      if (raw) continue;
      const start = line.from + m.index!, end = start + m[0].length, labelEnd = start + 1 + m[1].length;
      decos.push(hide.range(start, start + 1), Decoration.mark({ class: 'cm-web-link', attributes: { 'data-href': m[2], title: m[2] } }).range(start + 1, labelEnd), hide.range(labelEnd, end));
    }
    // Audio links get a player under the line.
    for (const a of line.text.matchAll(AUDIO)) {
      decos.push(Decoration.widget({ widget: new AudioWidget(a[2], a[1]), side: 1, block: true }).range(line.to));
    }
    // A chip after each PDF link, so a citation next to the file's link gets its own.
    for (const pdf of line.text.matchAll(PDF)) {
      const at = line.from + pdf.index! + pdf[0].length;
      decos.push(Decoration.widget({ widget: new PdfChip(pdf[2], pdf[1], pdf[3] ?? null), side: 1 }).range(at));
    }
  }
  return Decoration.set(decos, true);
}

/** What the media extension needs to know about the note it is showing. */
export interface MediaHooks {
  /** The path of the note being edited, so a relative link resolves the way the other app means. */
  notePath?: () => string | null;
  /** The note at a path, for a link to a file next to this one. */
  findByPath?: (path: string) => { id: string; title: string } | null;
  /** Opens a note by id. */
  openLink?: (target: string, event: MouseEvent) => void;
}

export function inlineMedia(hooks: MediaHooks = {}): Extension {
  return StateField.define<DecorationSet>({
    create: (state) => build(state, hooks),
    update(v, tr) {
      if (tr.docChanged) return build(tr.state, hooks);
      // Moving the cursor to another line shows that line's Markdown and tidies the one it left.
      if (tr.selection && tr.startState.doc.lineAt(tr.startState.selection.main.head).number !== tr.state.doc.lineAt(tr.state.selection.main.head).number) return build(tr.state, hooks);
      return v;
    },
    provide: (f) => EditorView.decorations.from(f)
  });
}

export const mediaTheme = EditorView.theme({
  '.cm-inline-image': { display: 'block', maxWidth: '100%', maxHeight: '420px', borderRadius: '8px', margin: '6px 0', border: '1px solid var(--line)' },
  '.cm-attach-name': { color: 'var(--accent)', textDecoration: 'underline', textUnderlineOffset: '2px' },
  '.cm-web-link': { color: 'var(--accent)', textDecoration: 'underline', textUnderlineOffset: '2px', cursor: 'pointer' },
  '.cm-audio': { display: 'flex', alignItems: 'center', gap: '8px', margin: '4px 0 8px' },
  '.cm-audio audio': { height: '36px', maxWidth: '100%', flex: '1 1 auto' },
  '.cm-audio-speed': { font: '600 11px var(--font-mono)', color: 'var(--text-muted)', background: 'var(--bg-raised)', border: '1px solid var(--line-strong)', borderRadius: '6px', padding: '3px 8px', cursor: 'pointer' },
  '.cm-pdf-chip': { marginLeft: '8px', font: '600 11px var(--font-mono)', color: 'var(--accent)', border: '1px solid var(--line-strong)', borderRadius: '10px', padding: '1px 8px', cursor: 'pointer' }
});
