import { setActiveEditor } from '../activeEditor';
import { StateField, type EditorState, type Range } from '@codemirror/state';
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
  mousedown(event, view) {
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
const PDF = /\[([^\]]*)\]\((attachments\/[^)\s#]+\.pdf)(?:#page=(\d+))?\)/gi;

// Links to stored files: [name](attachments/ab/abcdef….ext), not images.
const ATTACHMENT_LINK = /(?<!!)\[([^\]]*)\]\((attachments\/[^)\s]+)\)/g;
const hide = Decoration.replace({});
const fileName = Decoration.mark({ class: 'cm-attach-name' });

/**
 * Images show below their line; PDF links get an Open chip. Away from the cursor's line, the storage
 * path is hidden (a link reads as its name, an image as the picture); on that line the Markdown
 * is shown as written, so it can be edited.
 */
function build(state: EditorState): DecorationSet {
  const decos: Range<Decoration>[] = [];
  const cursorLine = state.doc.lineAt(state.selection.main.head).number;
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
    // A chip after each PDF link, so a citation next to the file's link gets its own.
    for (const pdf of line.text.matchAll(PDF)) {
      const at = line.from + pdf.index! + pdf[0].length;
      decos.push(Decoration.widget({ widget: new PdfChip(pdf[2], pdf[1], pdf[3] ?? null), side: 1 }).range(at));
    }
  }
  return Decoration.set(decos, true);
}

export const inlineMedia = StateField.define<DecorationSet>({
  create: build,
  update(v, tr) {
    if (tr.docChanged) return build(tr.state);
    // Moving the cursor to another line shows that line's Markdown and tidies the one it left.
    if (tr.selection && tr.startState.doc.lineAt(tr.startState.selection.main.head).number !== tr.state.doc.lineAt(tr.state.selection.main.head).number) return build(tr.state);
    return v;
  },
  provide: (f) => EditorView.decorations.from(f)
});

export const mediaTheme = EditorView.theme({
  '.cm-inline-image': { display: 'block', maxWidth: '100%', maxHeight: '420px', borderRadius: '8px', margin: '6px 0', border: '1px solid var(--line)' },
  '.cm-attach-name': { color: 'var(--accent)', textDecoration: 'underline', textUnderlineOffset: '2px' },
  '.cm-pdf-chip': { marginLeft: '8px', font: '600 11px var(--font-mono)', color: 'var(--accent)', border: '1px solid var(--line-strong)', borderRadius: '10px', padding: '1px 8px', cursor: 'pointer' }
});
