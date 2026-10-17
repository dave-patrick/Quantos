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
  // Opens by itself when it has text (or is today), but never closes by itself: a note created by
  // typing into an empty day is still "empty" for a moment and must stay open under the cursor.
  let expanded = $state(false);
  $effect(() => { if (isToday || !empty) expanded = true; });

  let doc = $state<Doc | null>(null);
  let pending = '';
  let creating: Promise<void> | null = null;
  // Typing started in the empty entry: the same editor stays (keeping focus and caret) after the
  // note is created. bump pushes the template text into it.
  let started = $state(false);
  let bump = $state(0);

  $effect(() => {
    if (node && !doc) doc = docs.acquire(node.id);
  });
  onDestroy(() => { if (doc) docs.release(doc.id); });

  async function onchange(text: string) {
    if (doc) { doc.edit(text); return; }
    pending = text;
    started = true;
    if (!creating) {
      creating = docs.track((async () => {
        try {
          const created = await api.post<NodeDto>(`daily/${workspace}/${date}`);
          const d = docs.acquire(created.id);
          await new Promise<void>((resolve) => {
            const wait = () => (d.status === 'loading' ? setTimeout(wait, 30) : resolve());
            wait();
          });
          // A daily template's text goes above what was typed, not under it or instead of it.
          const template = d.body.trim();
          d.edit(template && !pending.startsWith(template) ? `${template}\n\n${pending}` : pending);
          doc = d;
          if (template) bump++;
          oncreated(created);
          await d.flush();
        } catch (e) { app.error(e); }
        finally { creating = null; }
      })());
    }
  }
</script>

<section class="day" class:today={isToday} class:collapsed={!expanded}>
  <header>
    <button class="toggle" aria-expanded={expanded} onclick={() => (expanded = !expanded)}>
      <Icon name={expanded ? 'chevron-down' : 'chevron-right'} size={14} />
      <span class="when">{dayLabel(date)}</span>
      <!-- Relative labels get the full date; dated labels already say it, so show the note's file date. -->
      <span class="date faint">{['Today', 'Yesterday', 'Tomorrow'].includes(dayLabel(date)) ? longDate(date) : date}</span>
      {#if !expanded && empty}<span class="faint empty">empty</span>{/if}
    </button>
    {#if node}
      <button class="icon-btn" title="Open as a note" onclick={(e) => openNode(node!.id, e.shiftKey ? 'other' : 'new', node!.title)}><Icon name="file-text" size={14} /></button>
    {/if}
  </header>
  {#if expanded}
    <div class="body">
      {#if !isFuture}<DayTasks {date} {isToday} noteId={node?.id ?? null} />{/if}
      {#if started || !node}
        <!-- One editor from the first keystroke on, so creating the note never takes focus away. -->
        <Editor text={doc?.body ?? ''} version={(doc?.externalVersion ?? 0) + bump} docId={doc?.id ?? null} {onchange} onsave={() => doc?.save()}
          placeholder={isToday ? 'Start today’s note…' : 'Nothing written. Click to add.'} />
        {#if doc?.status === 'conflict'}
          <p class="warn">This entry changed elsewhere with overlapping edits. <button class="btn" onclick={(e) => openNode(doc!.id, e.shiftKey ? 'other' : 'new')}>Compare versions</button></p>
        {/if}
      {:else if doc && doc.status !== 'loading'}
        <Editor text={doc.body} version={doc.externalVersion} docId={doc.id} onchange={(t) => doc!.edit(t)} onsave={() => doc!.save()}
          placeholder={isToday ? 'What happened today? Type [[ to link, - [ ] for a task.' : ''} />
        {#if doc.status === 'conflict'}
          <p class="warn">This entry changed elsewhere with overlapping edits. <button class="btn" onclick={(e) => openNode(doc!.id, e.shiftKey ? 'other' : 'new')}>Compare versions</button></p>
        {/if}
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
  /* Entries stack in one scrolling stream, so the note editor's scroll-past-end space would leave gaps. */
  .body :global(.cm-content) { padding-bottom: 14px; }
  .warn { color: var(--warn); font-size: 13.5px; display: flex; gap: 10px; align-items: center; }
</style>
