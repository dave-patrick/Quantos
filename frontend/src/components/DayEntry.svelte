<script lang="ts">
  import { onDestroy } from 'svelte';
  import { api } from '../lib/api';
  import { docs, type Doc } from '../lib/docs.svelte';
  import { dayLabel, longDate } from '../lib/dates';
  import { app } from '../lib/state.svelte';
  import { openNode } from '../lib/links.svelte';
  import type { NodeDto } from '../lib/types';
  import Editor from './Editor.svelte';
  import Icon from './Icon.svelte';
  import DayTasks from './DayTasks.svelte';

  let { date, node, workspace, isToday, isFuture, oncreated }:
    { date: string; node: NodeDto | null; workspace: string; isToday: boolean; isFuture: boolean; oncreated: (n: NodeDto) => void } = $props();

  // Empty daily entries collapse (confirmed); today is always open.
  const empty = $derived(!node || node.body.trim() === '');
  let expanded = $state(false);
  $effect(() => { expanded = isToday || !empty; });

  let doc = $state<Doc | null>(null);
  let pending = '';
  let creating: Promise<void> | null = null;

  $effect(() => {
    if (node && !doc) doc = docs.acquire(node.id);
  });
  onDestroy(() => { if (doc) docs.release(doc.id); });

  async function onchange(text: string) {
    if (doc) { doc.edit(text); return; }
    pending = text;
    if (!creating) {
      creating = (async () => {
        try {
          const created = await api.post<NodeDto>(`daily/${workspace}/${date}`);
          oncreated(created);
          doc = docs.acquire(created.id);
          await new Promise<void>((resolve) => {
            const wait = () => (doc!.status === 'loading' ? setTimeout(wait, 30) : resolve());
            wait();
          });
          doc.edit(pending);
        } catch (e) { app.error(e); }
        finally { creating = null; }
      })();
    }
  }
</script>

<section class="day" class:today={isToday} class:collapsed={!expanded}>
  <header>
    <button class="toggle" aria-expanded={expanded} onclick={() => (expanded = !expanded)}>
      <Icon name={expanded ? 'chevron-down' : 'chevron-right'} size={14} />
      <span class="when">{dayLabel(date)}</span>
      <span class="date faint">{longDate(date)}</span>
      {#if !expanded && empty}<span class="faint empty">empty</span>{/if}
    </button>
    {#if node}
      <button class="icon-btn" title="Open as a note" onclick={(e) => openNode(node!.id, e.shiftKey ? 'other' : 'new', node!.title)}><Icon name="file-text" size={14} /></button>
    {/if}
  </header>
  {#if expanded}
    <div class="body">
      {#if !isFuture}<DayTasks {date} {isToday} />{/if}
      {#if doc && doc.status !== 'loading'}
        <Editor text={doc.body} version={doc.externalVersion} onchange={(t) => doc!.edit(t)} onsave={() => doc!.save()}
          placeholder={isToday ? 'What happened today? Type [[ to link, - [ ] for a task.' : ''} />
        {#if doc.status === 'conflict'}
          <p class="warn">This entry changed elsewhere with overlapping edits. <button class="btn" onclick={(e) => openNode(doc!.id, e.shiftKey ? 'other' : 'new')}>Compare versions</button></p>
        {/if}
      {:else if !node}
        <Editor text="" {onchange} placeholder={isToday ? 'Start today’s note…' : 'Nothing written. Click to add.'} />
      {/if}
    </div>
  {/if}
</section>

<style>
  .day { border-top: 1px solid var(--line); padding: 6px 0 10px; }
  .day.collapsed { padding: 2px 0; }
  header { display: flex; align-items: center; justify-content: space-between; }
  .toggle { display: flex; align-items: baseline; gap: 10px; border: 0; background: none; padding: 8px 4px; color: var(--text-soft); text-align: left; flex: 1; border-radius: 6px; }
  .toggle:hover { color: var(--text); }
  .toggle :global(.icon) { align-self: center; color: var(--text-faint); }
  .when { font: 700 17px/1.2 var(--font-display); letter-spacing: .01em; }
  .today .when { color: var(--accent-strong); }
  .collapsed .when { font-size: 14px; color: var(--text-muted); }
  .date { font-size: 12.5px; }
  .empty { font-size: 12px; font-family: var(--font-mono); }
  .body { padding-left: 4px; }
  .warn { color: var(--warn); font-size: 13.5px; display: flex; gap: 10px; align-items: center; }
</style>
