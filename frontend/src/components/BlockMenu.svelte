<script lang="ts">
  import { editorMenu, closeBlockMenu } from '../lib/editorMenu.svelte';
  import { tasks, type TaskStatus } from '../lib/tasks.svelte';
  import { app } from '../lib/state.svelte';
  import { api } from '../lib/api';
  import { setMarker, blockSource } from '../lib/editor/blockinfo';
  import ContextMenu, { type ContextMenuItem } from './ContextMenu.svelte';

  /** The right-click menu for a block: what can be done with the block under the pointer. */
  const block = $derived(editorMenu.block);
  const view = $derived(editorMenu.view);
  const isTask = $derived(block?.kind === 'task');
  const taskInfo = $derived(block?.taskId ? tasks.info(block.taskId) : undefined);
  const checked = $derived(isTask ? (taskInfo && !taskInfo.deleted ? taskInfo.status === 'done' : !!block?.checked) : false);
  /** A task line is bound to a task node once the note has been saved; until then it is just a box. */
  const bound = $derived(!!block?.taskId);

  function copy(b: NonNullable<typeof block>, v: NonNullable<typeof view>) {
    navigator.clipboard.writeText(blockSource(v.state, b)).catch((e) => app.error(e));
  }

  function duplicate(b: NonNullable<typeof block>, v: NonNullable<typeof view>) {
    const text = v.state.doc.sliceString(b.lineFrom, b.lineTo + 1);
    v.dispatch({ changes: { from: b.lineTo + 1, insert: '\n' + text } });
  }

  function remove(b: NonNullable<typeof block>, v: NonNullable<typeof view>) {
    const to = b.lineTo + 1;
    v.dispatch({ changes: { from: b.lineFrom, to: Math.min(to, v.state.doc.length) } });
  }

  function move(b: NonNullable<typeof block>, v: NonNullable<typeof view>, delta: number) {
    const state = v.state;
    const line = state.doc.lineAt(b.lineFrom);
    const target = delta < 0 ? line.number - 1 : line.number + 1;
    if (target < 1 || target > state.doc.lines) return;
    const other = state.doc.line(target);
    const text = state.doc.sliceString(b.lineFrom, b.lineTo + 1);
    const from = delta < 0 ? other.from : b.lineTo + 1;
    const to = delta < 0 ? b.lineTo + 1 : other.to + 1;
    v.dispatch({ changes: { from, to, insert: delta < 0 ? text + '\n' : '\n' + text } });
  }

  function select(b: NonNullable<typeof block>, v: NonNullable<typeof view>) {
    v.dispatch({ selection: { anchor: b.lineFrom, head: b.lineTo } });
  }

  function turnInto(b: NonNullable<typeof block>, v: NonNullable<typeof view>, marker: string) {
    setMarker(v, b, marker);
  }

  function status(taskId: string, next: TaskStatus) {
    tasks.setStatus(taskId, next).catch((e) => app.error(e));
  }

  /** Ticks the box in the text when the line isn't bound to a task yet. */
  function tick(b: NonNullable<typeof block>, v: NonNullable<typeof view>) {
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
    const b = block;
    const v = view;
    if (!b || !v) return [];
    const out: ContextMenuItem[] = [];
    if (isTask) {
      out.push({ label: checked ? 'Mark as not done' : 'Mark as done', icon: 'check', run: () => tick(b, v) });
      if (bound && b.taskId) {
        out.push({ label: 'Set to doing', icon: 'play', run: () => status(b.taskId!, 'doing') });
        out.push({ label: 'Set to waiting', icon: 'clock', run: () => status(b.taskId!, 'waiting') });
        out.push({ label: 'Set a due date…', icon: 'calendar', run: () => setDue(b.taskId!) });
        out.push({ label: 'Set a priority…', icon: 'flag', run: () => setPriority(b.taskId!) });
        out.push({ label: 'Move the task to trash', icon: 'trash', danger: true, run: () => trashTask(b.taskId!) });
      }
      out.push({ separator: true });
    }
    out.push({ label: 'Copy this block', icon: 'copy', run: () => copy(b, v) });
    out.push({ label: 'Select this block', icon: 'text', run: () => select(b, v) });
    out.push({ label: 'Duplicate', icon: 'copy', run: () => duplicate(b, v) });
    out.push({ label: 'Move up', icon: 'arrow-up', run: () => move(b, v, -1) });
    out.push({ label: 'Move down', icon: 'arrow-down', run: () => move(b, v, 1) });
    out.push({ separator: true });
    out.push({ group: 'Turn into' });
    out.push({ label: 'Task', icon: 'check-square', run: () => turnInto(b, v, '- [ ] ') });
    out.push({ label: 'Heading', icon: 'text', run: () => turnInto(b, v, '## ') });
    out.push({ label: 'Bullet', icon: 'list', run: () => turnInto(b, v, '- ') });
    out.push({ label: 'Quote', icon: 'inbox', run: () => turnInto(b, v, '> ') });
    out.push({ separator: true });
    out.push({ label: 'Delete this block', icon: 'trash', danger: true, run: () => remove(b, v) });
    return out;
  });
</script>

{#if editorMenu.open && block}
  <ContextMenu x={editorMenu.x} y={editorMenu.y} {items} onclose={closeBlockMenu} />
{/if}

