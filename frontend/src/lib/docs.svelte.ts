import { api, ApiError } from './api';
import { onChange } from './events';
import { layout, setTabFlusher } from './layout.svelte';
import { app } from './state.svelte';
import { merge3 } from './text';
import type { ChangeEvent, NodeDto, SaveResult } from './types';

export type DocStatus = 'loading' | 'saved' | 'dirty' | 'saving' | 'conflict' | 'error' | 'readonly' | 'trashed' | 'missing';

export interface Conflict { mine: string; disk: NodeDto; reason: string }

const SAVE_DELAY = 700;

/**
 * One open node. Edits are saved through the backend's single write path with the hash the editor
 * last saw. If the file changed meanwhile, non-overlapping changes merge automatically; overlapping
 * ones open the side-by-side comparison and nothing is overwritten.
 */
export class Doc {
  node = $state<NodeDto | null>(null);
  body = $state('');            // what the editor shows
  base = $state('');            // body as last synced with disk
  hash = $state('');
  status = $state<DocStatus>('loading');
  error = $state<string | null>(null);
  conflict = $state<Conflict | null>(null);
  trashed = $state<{ entryId: string; title: string } | null>(null);
  raw = $state(false);
  rawText = $state('');
  rawBaseHash = '';
  /** Bumped when the body changes from outside the editor, so the editor applies it. */
  externalVersion = $state(0);
  refs = 0;

  private timer: ReturnType<typeof setTimeout> | null = null;
  private chain: Promise<unknown> = Promise.resolve();

  constructor(public id: string) {}

  get dirty() { return this.raw ? this.rawText !== this.node?.content : this.body !== this.base; }

  async load() {
    try {
      const node = await api.get<NodeDto>(`nodes/${this.id}`);
      this.accept(node, true);
      this.status = node.readOnly ? 'readonly' : 'saved';
      if (node.readOnly) this.raw = true;
      this.rawText = node.content;
      this.rawBaseHash = node.hash;
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) this.status = 'missing';
      else { this.status = 'error'; this.error = (err as Error).message; }
    }
  }

  private accept(node: NodeDto, replaceBody: boolean) {
    this.node = node;
    this.hash = node.hash;
    if (replaceBody) {
      this.base = node.body;
      if (this.body !== node.body) { this.body = node.body; this.externalVersion++; }
    }
    layout.retitle(node.id, node.title);
  }

  /** Called by the editor on every change. */
  edit(body: string) {
    if (body === this.body) return;
    this.body = body;
    if (this.status !== 'conflict' && this.status !== 'readonly') this.status = 'dirty';
    this.schedule();
  }

  editRaw(text: string) {
    this.rawText = text;
    if (this.status !== 'conflict') this.status = 'dirty';
    this.schedule();
  }

  private schedule() {
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.save(), SAVE_DELAY);
  }

  /** Saves now (used before closing a tab, switching workspace or exiting). */
  flush(): Promise<unknown> {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
    if (!this.dirty || this.status === 'conflict') return this.chain;
    return this.save();
  }

  save(): Promise<unknown> {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
    this.chain = this.chain.then(() => (this.raw ? this.doSaveRaw() : this.doSave())).catch((err) => {
      this.status = 'error';
      this.error = (err as Error).message;
    });
    return this.chain;
  }

  private async doSave(): Promise<void> {
    if (!this.dirty || this.status === 'conflict' || this.status === 'readonly' || this.trashed) return;
    const sent = this.body;
    this.status = 'saving';
    try {
      const result = await api.patch<SaveResult>(`nodes/${this.id}`, { baseHash: this.hash, body: sent });
      if (result.node) this.afterSave(result.node, sent);
      this.status = this.dirty ? 'dirty' : 'saved';
      if (this.dirty) this.schedule();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) return this.resolveConflict(err.body as SaveResult, sent);
      if (err instanceof ApiError && err.status === 404) { this.status = 'missing'; return; }
      if (err instanceof ApiError && err.status === 422) { this.status = 'error'; this.error = err.message; return; }
      throw err;
    }
  }

  /** The server may adjust the body (resolving [[Title]] links). Fold that in without losing typing since. */
  private afterSave(node: NodeDto, sent: string) {
    this.node = node;
    this.hash = node.hash;
    this.rawText = node.content;
    this.rawBaseHash = node.hash;
    if (node.body === sent) { this.base = sent; return; }
    const merged = merge3(sent, this.body, node.body);
    this.base = node.body;
    if (merged !== null && merged !== this.body) { this.body = merged; this.externalVersion++; }
  }

  private async resolveConflict(result: SaveResult, sent: string): Promise<void> {
    const disk = await api.get<NodeDto>(`nodes/${this.id}`);
    const merged = merge3(this.base, this.body, disk.body);
    if (merged !== null) {
      // Non-overlapping edits (for example a capture appended to today's note): merge and save again.
      this.base = disk.body;
      this.hash = disk.hash;
      this.node = disk;
      if (merged !== this.body) { this.body = merged; this.externalVersion++; }
      this.status = 'dirty';
      return this.doSave();
    }
    this.conflict = { mine: sent, disk, reason: result?.message ?? 'The file changed on disk.' };
    this.status = 'conflict';
  }

  private async doSaveRaw() {
    if (this.rawText === this.node?.content) { this.status = 'saved'; return; }
    this.status = 'saving';
    try {
      const result = await api.put<SaveResult>(`nodes/${this.id}`, { content: this.rawText, baseHash: this.rawBaseHash });
      if (result.node) {
        this.accept(result.node, true);
        this.rawBaseHash = result.node.hash;
        if (result.node.content !== this.rawText) this.rawText = result.node.content;
        this.externalVersion++;
      }
      this.status = 'saved';
      this.error = null;
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        const disk = await api.get<NodeDto>(`nodes/${this.id}`);
        this.conflict = { mine: this.rawText, disk, reason: 'The file changed on disk.' };
        this.status = 'conflict';
        return;
      }
      if (err instanceof ApiError && err.status === 422) { this.status = 'error'; this.error = err.message; return; }
      throw err;
    }
  }

  /**
   * Resolve a conflict: the chosen version becomes the main copy; the other is always kept as a
   * recoverable history version, so no choice throws content away.
   */
  async resolve(choice: 'mine' | 'disk') {
    const conflict = this.conflict;
    if (!conflict) return;
    const mineIsRaw = this.raw;
    if (choice === 'mine') {
      await api.post(`nodes/${this.id}/preserve`, { content: conflict.disk.content, label: 'disk version from conflict' });
      this.hash = conflict.disk.hash;
      this.rawBaseHash = conflict.disk.hash;
      this.conflict = null;
      this.status = 'dirty';
      if (mineIsRaw) { this.rawText = conflict.mine; await this.doSaveRaw(); }
      else { this.base = conflict.disk.body; this.body = conflict.mine; await this.doSave(); }
    } else {
      const myContent = mineIsRaw ? conflict.mine : replaceBody(conflict.disk.content, conflict.disk.body, conflict.mine);
      await api.post(`nodes/${this.id}/preserve`, { content: myContent, label: 'app version from conflict' });
      this.conflict = null;
      this.accept(conflict.disk, true);
      this.rawText = conflict.disk.content;
      this.rawBaseHash = conflict.disk.hash;
      this.externalVersion++;
      this.status = 'saved';
    }
  }

  async setProperties(set: Record<string, unknown>, remove: string[] = []) {
    await this.flush();
    this.chain = this.chain.then(async () => {
      for (let attempt = 0; attempt < 2; attempt++) {
        try {
          const result = await api.patch<SaveResult>(`nodes/${this.id}`, { baseHash: this.hash, set, remove });
          if (result.node) {
            this.node = result.node;
            this.hash = result.node.hash;
            this.rawText = result.node.content;
            this.rawBaseHash = result.node.hash;
          }
          return;
        } catch (err) {
          if (err instanceof ApiError && err.status === 409 && attempt === 0) {
            await this.refresh();
            continue;
          }
          app.error(err);
          return;
        }
      }
    });
    return this.chain;
  }

  async rename(title: string) {
    await this.flush();
    const node = await api.post<NodeDto>(`nodes/${this.id}/rename`, { title });
    this.accept(node, !this.dirty);
  }

  /** An external change arrived (another window, the file watcher, a capture). */
  async refresh() {
    const disk = await api.get<NodeDto>(`nodes/${this.id}`);
    if (disk.hash === this.hash) return;
    if (this.raw) {
      if (this.rawText === this.node?.content) { this.accept(disk, true); this.rawText = disk.content; this.rawBaseHash = disk.hash; }
      else { this.conflict = { mine: this.rawText, disk, reason: 'The file changed on disk while you were editing the source.' }; this.status = 'conflict'; }
      return;
    }
    if (!this.dirty) {
      this.accept(disk, true);
      this.rawText = disk.content;
      this.rawBaseHash = disk.hash;
      return;
    }
    const merged = merge3(this.base, this.body, disk.body);
    if (merged === null) {
      this.conflict = { mine: this.body, disk, reason: 'The file changed on disk while you had unsaved edits.' };
      this.status = 'conflict';
      return;
    }
    this.node = disk;
    this.hash = disk.hash;
    this.base = disk.body;
    if (merged !== this.body) { this.body = merged; this.externalVersion++; }
    this.schedule();
  }

  setRaw(raw: boolean) {
    if (raw === this.raw) return;
    if (raw) {
      this.flush().then(() => {
        this.rawText = this.node?.content ?? '';
        this.rawBaseHash = this.hash;
        this.raw = true;
      });
    } else {
      if (this.node?.readOnly) return;
      this.flush().then(() => { this.raw = false; this.externalVersion++; });
    }
  }
}

function replaceBody(content: string, body: string, newBody: string) {
  return content.endsWith(body) ? content.slice(0, content.length - body.length) + newBody : newBody;
}

class DocStore {
  private docs = new Map<string, Doc>();

  get(id: string): Doc {
    let doc = this.docs.get(id);
    if (!doc) {
      doc = new Doc(id);
      this.docs.set(id, doc);
      doc.load();
    }
    return doc;
  }

  peek(id: string) { return this.docs.get(id); }

  acquire(id: string) {
    const doc = this.get(id);
    doc.refs++;
    return doc;
  }

  release(id: string) {
    const doc = this.docs.get(id);
    if (!doc) return;
    doc.refs--;
    if (doc.refs <= 0) {
      doc.flush().finally(() => { if (doc.refs <= 0 && !doc.dirty) this.docs.delete(id); });
    }
  }

  async flushAll() {
    await Promise.all([...this.docs.values()].map((d) => d.flush()));
  }

  pendingCount() {
    return [...this.docs.values()].filter((d) => d.dirty || d.status === 'saving').length;
  }

  handle(event: ChangeEvent) {
    if (!event.id) return;
    const doc = this.docs.get(event.id);
    if (!doc) return;
    if (event.kind === 'Trashed') {
      doc.trashed = { entryId: event.data?.entryId, title: event.data?.title ?? doc.node?.title ?? 'Untitled' };
      doc.status = 'trashed';
      return;
    }
    if (event.kind === 'Restored') {
      doc.trashed = null;
      doc.load();
      return;
    }
    if (event.kind === 'Deleted') { doc.status = 'missing'; return; }
    if (app.isOwnEvent(event.origin)) return;
    if (event.hash && event.hash === doc.hash) return;
    if (['Updated', 'Moved', 'Archived', 'Unarchived', 'Created'].includes(event.kind)) doc.refresh().catch(() => {});
  }
}

export const docs = new DocStore();
onChange((e) => docs.handle(e));
setTabFlusher(async (tab) => {
  if (tab.nodeId) await docs.peek(tab.nodeId)?.flush();
});
window.addEventListener('beforeunload', (e) => {
  if (docs.pendingCount() > 0) {
    docs.flushAll();
    e.preventDefault();
  }
});
