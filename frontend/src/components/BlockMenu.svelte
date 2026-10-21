<script lang="ts">
  import { editorMenu, closeBlockMenu } from '../lib/editorMenu.svelte';
  import { tasks, type TaskStatus } from '../lib/tasks.svelte';
  import { app } from '../lib/state.svelte';
  import { api } from '../lib/api';
  import { setMarker, type BlockInfo } from '../lib/editor/blockinfo';
  import { EditorSelection } from '@codemirror/state';
  import type { EditorView } from '@codemirror/view';
  import ContextMenu, { type ContextMenuItem } from './ContextMenu.svelte';

  /** The right-click menu for a block: what can be done with the block, or the blocks, under the pointer. */
  const block = $derived(editorMenu.block);
  const blocks = $derived(editorMenu.blocks);
  const view = $derived(editorMenu.view);
  const one = $derived(blocks.length === 1);
  const isTask = $derived(one && blocks[0]?.kind === 'task');
  const taskInfo = $derived(block?.taskId ? tasks.info(block.taskId) : undefined);
  const checked = $derived(isTask ? (taskInfo && !taskInfo.deleted ? taskInfo.status === 'done' : !!block?.checked) : false);
  /** A task line is bound to a task node once the note has been saved; until then it is just a box. */
  const bound = $derived(!!block?.taskId);

  /** The lines a block covers. */
  function lines(v: EditorView, b: BlockInfo): { from: number; to: number; first: number; last: number } {
    const first = v.state.doc.lineAt(b.lineFrom);
    const last = v.state.doc.lineAt(b.lineTo);
    return { from: first.from, to: last.to, first: first.number, last: last.number };
  }

  /** True when the blocks sit one after the other, so they can be moved as a group. */
  const together = $derived.by(() => {
    if (!view || blocks.length < 2) return true;
    for (let i = 1; i < blocks.length; i++) {
      const prev = lines(view, blocks[i - 1]);
      const next = lines(view, blocks[i]);
      if (next.first !== prev.last + 1) return false;
    }
    return true;
  });

  /** The text of a block, without the newline that follows it. */
  function text(v: EditorView, b: BlockInfo): string {
    const l = lines(v, b);
    return v.state.doc.sliceString(l.from, l.to);
  }

  function copy(v: EditorView, list: BlockInfo[]) {
    const all = list.map((b) => text(v, b)).join('\n');
    navigator.clipboard.writeText(all).catch((e) => app.error(e));
  }

  /** Puts a copy of each block right after it. */
  function duplicate(v: EditorView, list: BlockInfo[]) {
    const changes = list.map((b) => {
      const l = lines(v, b);
      const at = l.to < v.state.doc.length ? l.to + 1 : l.to;
      const copy = text(v, b);
      return { from: at, insert: l.to < v.state.doc.length ? copy + '\n' : '\n' + copy };
    });
    v.dispatch({ changes });
  }

  /** Takes the blocks out of the note. */
  function remove(v: EditorView, list: BlockInfo[]) {
    const changes = list.map((b) => {
      const l = lines(v, b);
      return { from: l.from, to: l.to < v.state.doc.length ? l.to + 1 : l.to };
    });
    v.dispatch({ changes });
  }

  /** Replaces a run of lines with other lines. */
  function replaceLines(v: EditorView, fromLine: number, toLine: number, texts: string[]) {
    const doc = v.state.doc;
    const first = doc.line(fromLine);
    const last = doc.line(toLine);
    const end = last.to < doc.length ? last.to + 1 : last.to;
    const insert = texts.join('\n') + (end > last.to ? '\n' : '');
    v.dispatch({ changes: { from: first.from, to: end, insert } });
  }

  /** Moves the blocks up or down, past the block above or below them. */
  function move(v: EditorView, list: BlockInfo[], delta: number) {
    if (!together) return;
    const first = lines(v, list[0]).first;
    const last = lines(v, list[list.length - 1]).last;
    const group: string[] = [];
    for (let n = first; n <= last; n++) group.push(v.state.doc.line(n).text);
    if (delta < 0) {
      if (first === 1) return;
      replaceLines(v, first - 1, last, [...group, v.state.doc.line(first - 1).text]);
    } else {
      if (last >= v.state.doc.lines) return;
      replaceLines(v, first, last + 1, [v.state.doc.line(last + 1).text, ...group]);
    }
  }

  /** Selects the blocks, so they can be dragged or worked on together. */
  function select(v: EditorView, list: BlockInfo[]) {
    const ranges = list.map((b) => {
      const l = lines(v, b);
      return EditorSelection.range(l.from, l.to);
    });
    v.dispatch({ selection: EditorSelection.create(ranges, ranges.length - 1) });
  }

  function turnInto(v: EditorView, list: BlockInfo[], marker: string) {
    for (const b of list) setMarker(v, b, marker);
  }

  function status(taskId: string, next: TaskStatus) {
    tasks.setStatus(taskId, next).catch((e) => app.error(e));
  }

  /** Ticks the box in the text when the line isn't bound to a task yet. */
  function tick(v: EditorView, list: BlockInfo[]) {
    const b = list[0];
    if (!b) return;
    if (b.taskId) {
      status(b.taskId, checked ? 'todo' : 'done');
      return;
    }
    const at = v.state.doc.lineAt(b.lineFrom).text.indexOf('[ ]');
    if (at < 0) return;
    const from = v.state.doc.lineAt(b.lineFrom).from + at;
    v.dispatch({ changes: { from, to: from + 3, insert: checked ? '[ ]' : '[x]' } });
  }

  async function setDue(taskId: string) {
    const value = await app.ask('Due date', { placeholder: 'YYYY-MM-DD', value: taskInfo?.due ?? '' });
    if (value === null) return;
    const clean = value.trim();
    tasks.update(taskId, clean ? { due: clean } : {}, clean ? [] : ['due']).catch((e) => app.error(e));
  }

  async function setPriority(taskId: string) {
    const value = await app.ask('Priority', { placeholder: '1 (highest) to 4', value: taskInfo?.priority ? String(taskInfo.priority) : '' });
    if (value === null) return;
    const n = Number(value.trim());
    tasks.update(taskId, n >= 1 && n <= 4 ? { priority: n } : {}, n >= 1 && n <= 4 ? [] : ['priority']).catch((e) => app.error(e));
  }

  /** A task is a note, so it goes to the trash like any other note, and can be brought back. */
  async function trashTask(taskId: string) {
    try {
      await api.post(`nodes/${taskId}/trash`);
      app.toast('Moved to trash.', 'ok', { label: 'Undo', run: async () => {
        const entries = await api.get<{ entryId: string; id: string }[]>('trash');
        const entry = entries.find((e) => e.id === taskId);
        if (entry) await api.post(`trash/${entry.entryId}/restore`);
        await tasks.load();
      } });
    } catch (e) { app.error(e); }
  }

  /** The menu, for the block it was opened for. */
  const items = $derived.by((): ContextMenuItem[] => {
    const v = view;
    // The menu is closed before an action runs, so the blocks are held here, not read later.
    const list = blocks;
    if (!v || !list.length) return [];
    const out: ContextMenuItem[] = [];
    const many = list.length > 1;
    if (isTask) {
      const b = list[0];
      out.push({ label: checked ? 'Mark as not done' : 'Mark as done', icon: 'check', run: () => tick(v, list) });
      if (bound && b.taskId) {
        out.push({ label: 'Set to doing', icon: 'play', run: () => status(b.taskId!, 'doing') });
        out.push({ label: 'Set to waiting', icon: 'clock', run: () => status(b.taskId!, 'waiting') });
        out.push({ label: 'Set a due date…', icon: 'calendar', run: () => setDue(b.taskId!) });
        out.push({ label: 'Set a priority…', icon: 'flag', run: () => setPriority(b.taskId!) });
        out.push({ label: 'Move the task to trash', icon: 'trash', danger: true, run: () => trashTask(b.taskId!) });
      }
      out.push({ separator: true });
    }
    out.push({ label: many ? `Copy ${list.length} blocks` : 'Copy this block', icon: 'copy', run: () => copy(v, list) });
    out.push({ label: many ? `Select ${list.length} blocks` : 'Select this block', icon: 'text', run: () => select(v, list) });
    out.push({ label: 'Duplicate', icon: 'copy', run: () => duplicate(v, list) });
    if (together) {
      out.push({ label: 'Move up', icon: 'arrow-up', run: () => move(v, list, -1) });
      out.push({ label: 'Move down', icon: 'arrow-down', run: () => move(v, list, 1) });
    }
    out.push({ separator: true });
    out.push({ group: 'Turn into' });
    out.push({ label: 'Task', icon: 'check-square', run: () => turnInto(v, list, '- [ ] ') });
    out.push({ label: 'Heading', icon: 'text', run: () => turnInto(v, list, '## ') });
    out.push({ label: 'Bullet', icon: 'list', run: () => turnInto(v, list, '- ') });
    out.push({ label: 'Quote', icon: 'inbox', run: () => turnInto(v, list, '> ') });
    out.push({ separator: true });
    out.push({ label: many ? `Delete ${list.length} blocks` : 'Delete this block', icon: 'trash', danger: true, run: () => remove(v, list) });
    return out;
  });
</script>

{#if editorMenu.open && blocks.length}
  <ContextMenu x={editorMenu.x} y={editorMenu.y} {items} onclose={closeBlockMenu} />
{/if}
