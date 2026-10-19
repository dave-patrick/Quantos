<script lang="ts">
  import { editorMenu, closeBlockMenu } from '../lib/editorMenu.svelte';
  import { activeEditor } from '../lib/activeEditor';
  import { tasks, type TaskStatus } from '../lib/tasks.svelte';
  import { app } from '../lib/state.svelte';
  import { setMarker, blockSource } from '../lib/editor/blockinfo';
  import { menu } from '../lib/menu';
  import Icon from './Icon.svelte';

  /** The right-click menu for a block: what can be done with the block under the pointer. */
  const block = $derived(editorMenu.block);
  const view = $derived(editorMenu.view);
  const isTask = $derived(block?.kind === 'task');
  const taskInfo = $derived(block?.taskId ? tasks.info(block.taskId) : undefined);
  const checked = $derived(isTask ? (taskInfo && !taskInfo.deleted ? taskInfo.status === 'done' : !!block?.checked) : false);
  /** A task line is bound to a task node once the note has been saved; until then it is just a box. */
  const bound = $derived(!!block?.taskId);

  /** Runs an action on the block the menu was opened for, then closes the menu. */
  function act(fn: (b: NonNullable<typeof block>, v: NonNullable<typeof view>) => void) {
    const b = block;
    const v = view;
    closeBlockMenu();
    if (!b || !v) return;
    fn(b, v);
    v.focus();
  }

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
</script>

{#if editorMenu.open && block}
  <button class="backdrop" aria-label="Close menu" onclick={closeBlockMenu}></button>
  <div class="menu" role="menu" tabindex="-1" style="left:{editorMenu.x}px; top:{editorMenu.y}px" use:menu={{ onclose: closeBlockMenu }}>
    {#if isTask}
      <button role="menuitem" onclick={() => act(tick)}>
        <Icon name="check" size={14} /> {checked ? 'Mark as not done' : 'Mark as done'}
      </button>
      {#if bound && block.taskId}
        <button role="menuitem" onclick={() => act((b) => status(b.taskId!, 'doing'))}><Icon name="play" size={14} /> Set to doing</button>
        <button role="menuitem" onclick={() => act((b) => status(b.taskId!, 'waiting'))}><Icon name="clock" size={14} /> Set to waiting</button>
        <button role="menuitem" onclick={() => act((b) => setDue(b.taskId!))}><Icon name="calendar" size={14} /> Set a due date…</button>
        <button role="menuitem" onclick={() => act((b) => setPriority(b.taskId!))}><Icon name="flag" size={14} /> Set a priority…</button>
      {/if}
      <hr />
    {/if}
    <button role="menuitem" onclick={() => act(copy)}><Icon name="copy" size={14} /> Copy this block</button>
    <button role="menuitem" onclick={() => act(select)}><Icon name="text" size={14} /> Select this block</button>
    <button role="menuitem" onclick={() => act(duplicate)}><Icon name="copy" size={14} /> Duplicate</button>
    <button role="menuitem" onclick={() => act((b, v) => move(b, v, -1))}><Icon name="arrow-up" size={14} /> Move up</button>
    <button role="menuitem" onclick={() => act((b, v) => move(b, v, 1))}><Icon name="arrow-down" size={14} /> Move down</button>
    <hr />
    <div class="group" role="group" aria-label="Turn into">
      <span class="label">Turn into</span>
      <button role="menuitem" onclick={() => act((b, v) => turnInto(b, v, '- [ ] '))}><Icon name="check-square" size={14} /> Task</button>
      <button role="menuitem" onclick={() => act((b, v) => turnInto(b, v, '## '))}><Icon name="text" size={14} /> Heading</button>
      <button role="menuitem" onclick={() => act((b, v) => turnInto(b, v, '- '))}><Icon name="list" size={14} /> Bullet</button>
      <button role="menuitem" onclick={() => act((b, v) => turnInto(b, v, '> '))}><Icon name="inbox" size={14} /> Quote</button>
    </div>
    <hr />
    <button role="menuitem" class="danger" onclick={() => act(remove)}><Icon name="trash" size={14} /> Delete this block</button>
  </div>
{/if}

<style>
  .backdrop { position: fixed; inset: 0; border: 0; background: transparent; z-index: 45; cursor: default; }
  .menu { position: fixed; z-index: 46; min-width: 210px; padding: 5px; border-radius: 8px; background: var(--bg-raised); border: 1px solid var(--line-strong); box-shadow: var(--shadow); display: grid; }
  .menu button { display: flex; align-items: center; gap: 8px; text-align: left; padding: 7px 10px; border: 0; border-radius: 5px; background: none; color: var(--text-soft); font-size: 13.5px; }
  .menu button:hover { background: var(--bg-hover); color: var(--text); }
  .menu button.danger:hover { color: var(--danger); }
  .menu .group { display: grid; }
  .group .label { padding: 6px 10px 2px; font: 600 10px/1 var(--font-mono); letter-spacing: .08em; text-transform: uppercase; color: var(--text-faint); }
  hr { border: 0; border-top: 1px solid var(--line); margin: 4px 0; }
</style>
