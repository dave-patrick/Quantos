<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { layout } from '../../lib/layout.svelte';
  import { views } from '../../lib/views.svelte';
  import { display } from '../../lib/query';

  /** Samples are stored as text; lists arrive as JSON. Show both the way tables do, with link titles. */
  const sample = (s: string) => { if (s.startsWith('[')) { try { return display(JSON.parse(s)); } catch { /* plain text */ } } return display(s); };
  interface Entry { key: string; count: number; kinds: string[]; nodeTypes: string[]; definedBy: string[]; samples: string[] }
  let entries = $state<Entry[]>([]);
  let scope = $state<'workspace' | 'all'>('workspace');
  let filter = $state('');

  async function load() { entries = await api.get<Entry[]>('dictionary' + (scope === 'workspace' ? `?workspace=${app.workspace}` : '')); }
  onMount(load);
  $effect(() => { void scope; load(); });

  const shown = $derived(entries.filter((e) => !filter.trim() || e.key.includes(filter.trim().toLowerCase())));

  async function explore(key: string) {
    const id = crypto.randomUUID();
    await views.save({ id, name: `Notes with ${key}`, query: { workspaces: scope === 'all' ? 'all' : 'active', filters: [{ field: key, op: 'notempty' }] }, layout: 'table', columns: ['title', key, 'type', 'modified'] });
    layout.open({ kind: 'view', arg: id, title: `Notes with ${key}` }, 'new');
  }
</script>

<div class="scroll view">
  <div class="inner">
    <h1>Metadata dictionary</h1>
    <p class="muted">Every property used in your notes, how often, what kinds of values it holds, and which supertags define it. Mixed kinds usually mean a typo or an old schema.</p>
    <div class="bar">
      <input class="input" aria-label="Filter properties" placeholder="Filter properties" bind:value={filter} />
      <select class="input" aria-label="Workspaces" bind:value={scope}><option value="workspace">{app.workspace}</option><option value="all">All workspaces</option></select>
    </div>
    <table>
      <thead><tr><th>Property</th><th>Used</th><th>Values</th><th>On</th><th>Defined by</th><th>Examples</th><th></th></tr></thead>
      <tbody>
        {#each shown as e (e.key)}
          <tr>
            <td class="mono">{e.key}</td>
            <td>{e.count}</td>
            <td>{#each e.kinds as k (k)}<span class="badge" class:warn={e.kinds.length > 1}>{k}</span> {/each}</td>
            <td class="faint">{e.nodeTypes.join(', ')}</td>
            <td>{#each e.definedBy as t (t)}<span class="badge accent">#{t}</span> {/each}</td>
            <td class="faint samples">{e.samples.map(sample).join(' · ')}</td>
            <td><button class="btn ghost" onclick={() => explore(e.key)}>Explore</button></td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .inner { max-width: 1200px; margin: 0 auto; padding: 24px 0 80px; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  .bar { display: flex; gap: 8px; margin: 14px 0; }
  .bar .input { width: auto; }
  table { width: 100%; border-collapse: collapse; font-size: 13.5px; }
  th { text-align: left; font: 600 11px var(--font-mono); letter-spacing: .07em; text-transform: uppercase; color: var(--text-faint); padding: 6px 8px; border-bottom: 1px solid var(--line); }
  td { padding: 7px 8px; border-bottom: 1px solid var(--line); vertical-align: top; }
  .mono { font-family: var(--font-mono); font-size: 12.5px; }
  .samples { max-width: 260px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
