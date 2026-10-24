<script lang="ts">
  import { commandList } from '../lib/commands.svelte';
  import { saveToolbar } from '../lib/customize.svelte';
  import { defaultToolbar, hiddenToolbarActionIds, toolbar, type ToolbarItem, visibleToolbarItems } from '../lib/toolbar.svelte';
  import { app } from '../lib/state.svelte';
  import Icon from './Icon.svelte';

  let items = $state<ToolbarItem[]>(visibleToolbarItems(toolbar.items).map((i) => ({ ...i })));
  const available = $derived(commandList.items.filter((c) => !hiddenToolbarActionIds.has(c.id) && !items.some((i) => i.id === c.id)).sort((a, b) => a.title.localeCompare(b.title)));

  function move(i: number, d: number) {
    const j = i + d;
    if (j < 0 || j >= items.length) return;
    [items[i], items[j]] = [items[j], items[i]];
  }

  async function save() { try { await saveToolbar($state.snapshot(items) as ToolbarItem[]); app.toast('Toolbar saved.', 'ok'); } catch (e) { app.error(e); } }
</script>

<h2>Toolbar</h2>
<p class="muted">Choose the quick actions shown at the top of the window. Saved in the vault's toolbar.json.</p>
{#each items as item, i (item.id)}
  <div class="row">
    <Icon name={item.icon ?? 'zap'} size={15} />
    <input class="input" bind:value={item.label} aria-label="Label" />
    <span class="faint">{item.title}</span>
    <button class="icon-btn" aria-label="Move up" onclick={() => move(i, -1)}><Icon name="arrow-left" size={13} /></button>
    <button class="icon-btn" aria-label="Move down" onclick={() => move(i, 1)}><Icon name="arrow-right" size={13} /></button>
    <button class="icon-btn" aria-label="Remove" onclick={() => (items = items.filter((x) => x.id !== item.id))}><Icon name="x" size={13} /></button>
  </div>
{/each}
<div class="add">
  <select class="input" onchange={(e) => {
    const c = commandList.items.find((x) => x.id === (e.target as HTMLSelectElement).value);
    if (c) items = [...items, { id: c.id, label: c.title.split(' ').slice(0, 2).join(' '), title: c.title, icon: c.icon }];
    (e.target as HTMLSelectElement).value = ''; }}>
    <option value="">Add an action…</option>
    {#each available as c (c.id)}<option value={c.id}>{c.group}: {c.title}</option>{/each}
  </select>
  <button class="btn" onclick={() => (items = defaultToolbar.map((i) => ({ ...i })))}>Restore defaults</button>
  <button class="btn primary" onclick={save}>Save toolbar</button>
</div>

<style>
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 8px; }
  .row { display: flex; gap: 8px; align-items: center; padding: 4px 0; }
  .row .input { width: 160px; height: 30px; }
  .row .faint { flex: 1; font-size: 13px; }
  .add { display: flex; gap: 8px; margin-top: 14px; }
  .add .input { width: auto; }
</style>
