import { RangeSetBuilder, StateField, type EditorState } from '@codemirror/state';
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

export const attachmentDrop = EditorView.domEventHandlers({
  paste(event, view) {
    const files = [...(event.clipboardData?.files ?? [])];
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
  mousedown(event) {
    const el = (event.target as HTMLElement).closest('[data-pdf]') as HTMLElement | null;
    if (!el) return false;
    event.preventDefault();
    layout.open({ kind: 'pdf', arg: el.dataset.pdf!, title: el.dataset.name ?? 'PDF' }, event.shiftKey ? 'other' : 'new');
    return true;
  }
});

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

const IMAGE = /!\[([^\]]*)\]\((attachments\/[^)\s]+\.(?:png|jpe?g|gif|webp|svg))\)/i;
const PDF = /\[([^\]]*)\]\((attachments\/[^)\s#]+\.pdf)(?:#page=(\d+))?\)/i;

function build(state: EditorState): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  for (let n = 1; n <= state.doc.lines; n++) {
    const line = state.doc.line(n);
    const img = IMAGE.exec(line.text);
    if (img) builder.add(line.to, line.to, Decoration.widget({ widget: new ImageWidget(img[2], img[1]), side: 1, block: true }));
    const pdf = PDF.exec(line.text);
    if (pdf && !img) builder.add(line.to, line.to, Decoration.widget({ widget: new PdfChip(pdf[2], pdf[1], pdf[3] ?? null), side: 1 }));
  }
  return builder.finish();
}

export const inlineMedia = StateField.define<DecorationSet>({
  create: build,
  update(v, tr) { return tr.docChanged ? build(tr.state) : v; },
  provide: (f) => EditorView.decorations.from(f)
});

export const mediaTheme = EditorView.theme({
  '.cm-inline-image': { display: 'block', maxWidth: '100%', maxHeight: '420px', borderRadius: '8px', margin: '6px 0', border: '1px solid var(--line)' },
  '.cm-pdf-chip': { marginLeft: '8px', font: '600 11px var(--font-mono)', color: 'var(--accent)', border: '1px solid var(--line-strong)', borderRadius: '10px', padding: '1px 8px', cursor: 'pointer' }
});
