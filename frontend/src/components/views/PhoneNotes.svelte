<script lang="ts">
  import { app } from '../../lib/state.svelte';
  import { openNode } from '../../lib/links.svelte';
  import { newNote } from '../../lib/commands.svelte';
  import { ago } from '../../lib/today.svelte';
  import Icon from '../Icon.svelte';
  let query = $state('');
  const notes = $derived(app.titles.filter(n => !n.archived && n.workspace === app.workspace && (!query.trim() || (n.title + ' ' + n.tags.join(' ')).toLowerCase().includes(query.trim().toLowerCase())))
    .sort((a,b) => (b.modified ?? '').localeCompare(a.modified ?? '')));
  async function create() { try { await newNote('current'); } catch(e) { app.error(e); } }
</script>

<section class="phone-notes">
  <header><h1>Notes</h1><p class="faint">{app.workspace}</p><input class="input" type="search" aria-label="Find a note" placeholder="Find a note…" bind:value={query} /></header>
  <div class="scroll note-list">
    {#each notes as n (n.id)}
      <button class="note-row" onclick={() => openNode(n.id, 'current', n.title)}>
        <span class="note-title">{n.title}</span><span class="note-meta">{n.type} {#if n.modified} · {ago(n.modified)}{/if}</span>
        <Icon name="chevron-right" size={18} />
      </button>
    {:else}
      <div class="empty-state"><h2>{query ? 'No matching notes' : 'Your notes start here'}</h2><p>{query ? 'Try another title, or use Search above to search note text.' : 'Create a note, or connect your shared vault in More › Settings.'}</p></div>
    {/each}
  </div>
  <button class="new-note" onclick={create}><Icon name="plus" size={24} /> New note</button>
</section>

<style>
  .phone-notes { display: flex; flex-direction: column; flex: 1; min-height: 0; position: relative; }
  header { padding: 20px 16px 12px; }
  h1 { font: 700 28px/1.2 var(--font-display); margin: 0 0 4px; }
  header p { font-size: 14px; margin-bottom: 16px; }
  header input { width: 100%; }
  .note-list { flex: 1; padding: 0 16px 96px; }
  .note-row { width: 100%; display: grid; grid-template-columns: minmax(0,1fr) 24px; gap: 6px 12px; padding: 16px 4px; text-align: left; border: 0; border-bottom: 1px solid var(--line); background: transparent; color: var(--text); }
  .note-row:hover { background: var(--bg-hover); }
  .note-title { font: 600 17px/1.4 var(--font-body); overflow-wrap: anywhere; }
  .note-meta { grid-column: 1; font-size: 13px; color: var(--text-muted); }
  .note-row :global(.icon) { grid-column: 2; grid-row: 1 / 3; align-self: center; color: var(--text-faint); }
  .new-note { position: absolute; bottom: 16px; right: 16px; display: flex; align-items: center; gap: 12px; min-height: 56px; padding: 0 20px; border: 0; border-radius: 16px; background: var(--accent); color: var(--accent-ink); font: 600 16px var(--font-body); box-shadow: var(--shadow); }
</style>
