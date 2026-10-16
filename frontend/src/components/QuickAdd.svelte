<script lang="ts">
  import { parseQuickAdd } from '../lib/quickadd';
  import { tasks } from '../lib/tasks.svelte';
  import { app } from '../lib/state.svelte';
  import { isoDate } from '../lib/dates';
  import Icon from './Icon.svelte';

  let { defaults = {}, placeholder = 'Add a task: Call Sam due:fri !2 ~30m #client every week' }:
    { defaults?: { planned?: string; due?: string; myDay?: boolean; parent?: string; project?: string; source?: string }; placeholder?: string } = $props();

  let text = $state('');
  const preview = $derived(text.trim() ? parseQuickAdd(text) : null);

  async function add() {
    const q = parseQuickAdd(text);
    if (!q.title) return;
    try {
      const node = await tasks.create({
        title: q.title, workspace: app.workspace, due: q.due ?? defaults.due ?? null, planned: q.planned ?? defaults.planned ?? null,
        priority: q.priority ?? null, effort: q.effort ?? null, recurrence: q.recurrence ?? null, parent: defaults.parent ?? null,
        project: q.project ?? defaults.project ?? null, source: defaults.source ?? null
      });
      if (q.tags.length) await tasks.update(node.id, { tags: q.tags });
      if (q.myDay || defaults.myDay) await tasks.update(node.id, { my_day: isoDate() });
      text = '';
    } catch (e) { app.error(e); }
  }
</script>

<form class="quick" onsubmit={(e) => { e.preventDefault(); add(); }}>
  <Icon name="plus" size={15} />
  <input bind:value={text} {placeholder} aria-label="Add a task" />
  {#if preview}
    <span class="preview">
      {#if preview.priority}<span>P{preview.priority}</span>{/if}
      {#if preview.due}<span>due {preview.due}</span>{/if}
      {#if preview.planned}<span>plan {preview.planned}</span>{/if}
      {#if preview.effort}<span>{preview.effort}m</span>{/if}
      {#if preview.recurrence}<span>↻ {preview.recurrence}</span>{/if}
      {#each preview.tags as t (t)}<span>#{t}</span>{/each}
    </span>
  {/if}
</form>

<style>
  .quick { display: flex; align-items: center; gap: 10px; padding: 0 12px; border: 1px dashed var(--line-strong); border-radius: 8px; color: var(--text-faint); margin: 8px 0; }
  .quick:focus-within { border-style: solid; border-color: var(--accent); }
  input { flex: 1; height: 38px; border: 0; background: transparent; font-size: 14.5px; }
  input:focus { outline: none; }
  .preview { display: flex; gap: 6px; font: 11px var(--font-mono); color: var(--accent); }
</style>
