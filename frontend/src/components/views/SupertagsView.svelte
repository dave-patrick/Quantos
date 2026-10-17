<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { layout, type Tab } from '../../lib/layout.svelte';
  import { supertags, type Supertag, type FieldType } from '../../lib/supertags.svelte';
  import { templatesStore } from '../../lib/templates.svelte';
  import QueryView from './QueryView.svelte';
  import FormDialog from '../FormDialog.svelte';
  import Icon from '../Icon.svelte';

  let { tab }: { tab: Tab } = $props();

  let selectedName = $state(tab.arg ?? '');
  const selected = $derived(supertags.byName(selectedName));
  let draft = $state<{ parent: string; color: string; icon: string; layout: string; template: string; autoApply: boolean; description: string; fields: { name: string; type: FieldType; options: string; required: boolean; description: string }[] } | null>(null);
  let newName = $state('');
  let newScope = $state('');
  let renameTo = $state('');
  let formOpen = $state(false);
  let migrate = $state<{ field: string; toType: string; toKey: string } | null>(null);

  const types: FieldType[] = ['text', 'number', 'date', 'datetime', 'checkbox', 'select', 'multiselect', 'node', 'nodes', 'person', 'url', 'email', 'status'];

  onMount(() => { supertags.load(); templatesStore.load(); });

  // The list reloads whenever any note changes, so a fresh copy of the selected tag arrives often.
  // Unsaved edits to it are kept; the draft is rebuilt only for another tag or when nothing is edited.
  let draftFor = '';
  let baseline = '';
  $effect(() => {
    const t = selected;
    untrack(() => {
      if (!t) { draft = null; draftFor = ''; return; }
      const fresh = {
        parent: t.parent ?? '', color: t.color ?? '', icon: t.icon ?? '', layout: t.layout ?? '', template: t.template ?? '', autoApply: !!t.autoApplyTemplate,
        description: t.description ?? '',
        fields: t.fields.map((f) => ({ name: f.name, type: f.type, options: (f.options ?? []).join(', '), required: !!f.required, description: f.description ?? '' }))
      };
      const edited = draft !== null && draftFor === t.id && JSON.stringify(draft) !== baseline;
      if (edited) return;
      draft = fresh;
      baseline = JSON.stringify(fresh);
      if (draftFor !== t.id) renameTo = t.name;
      draftFor = t.id;
    });
  });

  function select(name: string) {
    selectedName = name;
    tab.arg = name;
    tab.title = name ? '#' + name : 'Supertags';
    layout.persist();
  }

  async function create() {
    const name = newName.trim().replace(/^#/, '');
    if (!name) return;
    try {
      await api.post('supertags', { name, scope: newScope, fields: [] });
      await supertags.load();
      newName = '';
      select(name);
    } catch (e) { app.error(e); }
  }

  async function saveDraft() {
    if (!selected || !draft) return;
    try {
      await api.put(`supertags/${selected.id}`, {
        parent: draft.parent, color: draft.color, icon: draft.icon, layout: draft.layout, template: draft.template, autoApplyTemplate: draft.autoApply,
        description: draft.description,
        fields: draft.fields.filter((f) => f.name.trim()).map((f) => ({
          name: f.name.trim(), type: f.type, required: f.required, description: f.description || undefined,
          options: f.type === 'select' || f.type === 'multiselect' || f.type === 'status' ? f.options.split(',').map((s) => s.trim()).filter(Boolean) : undefined
        }))
      });
      baseline = JSON.stringify(draft); // saved: the next copy from the server replaces the draft
      await supertags.load();
      app.toast('Supertag saved. Existing values were kept as they are.', 'ok');
    } catch (e) { app.error(e); }
  }

  async function rename() {
    if (!selected || !renameTo.trim() || renameTo.trim() === selected.name) return;
    const ok = await app.confirm(`Rename #${selected.name}?`, `Every note tagged #${selected.name} and every #${selected.name} in note text will change to #${renameTo.trim()}. Code blocks and history are left as they were.`, 'Rename');
    if (!ok) return;
    try {
      await api.post(`supertags/${selected.id}/rename`, { title: renameTo.trim() });
      await supertags.load();
      select(renameTo.trim().replace(/^#/, ''));
    } catch (e) { app.error(e); }
  }

  async function runMigration() {
    if (!selected || !migrate) return;
    try {
      const r = await api.post<{ changed: number; skipped: number }>(`supertags/${selected.id}/migrate`, { field: migrate.field, toType: migrate.toType || null, toKey: migrate.toKey || null });
      app.toast(`Converted ${r.changed} value(s). ${r.skipped ? `${r.skipped} couldn't be converted and were kept as they were.` : ''}`, 'ok');
      migrate = null;
    } catch (e) { app.error(e); }
  }
</script>

<div class="tags">
  <aside class="scroll list">
    <form class="new" onsubmit={(e) => { e.preventDefault(); create(); }}>
      <input class="input" aria-label="New supertag name" bind:value={newName} placeholder="New supertag" />
      <select class="input" aria-label="Scope" bind:value={newScope} title="Scope"><option value="">Global</option>{#each app.workspaces as w (w)}<option value={w}>{w} only</option>{/each}</select>
      <button class="btn primary" type="submit">Add</button>
    </form>
    {#each supertags.tree as entry (entry.tag.id)}
      <button class="item" class:on={entry.tag.name === selectedName} style="padding-left:{10 + entry.depth * 16}px" onclick={() => select(entry.tag.name)}>
        <span class="swatch" style="background:{entry.tag.color ?? 'var(--accent)'}"></span> #{entry.tag.name}
        {#if entry.tag.scope}<span class="faint scope">{entry.tag.scope}</span>{/if}
        <span class="faint count">{entry.count}</span>
      </button>
    {:else}
      <p class="faint pad">No supertags yet. A supertag gives notes a type with fields, like #meeting with Date and Attendees.</p>
    {/each}
  </aside>

  <section class="scroll detail">
    {#if selected && draft}
      <header>
        <h1 style="color:{selected.color ?? 'var(--text)'}">#{selected.name}</h1>
        <div class="tools">
          <button class="btn primary" onclick={() => (formOpen = true)}><Icon name="plus" size={14} /> New #{selected.name}</button>
          <button class="btn" onclick={saveDraft}>Save changes</button>
        </div>
      </header>
      {#if selected.error}<p class="warn"><Icon name="alert" size={14} /> {selected.error}</p>{/if}

      <div class="grid">
        <label>Inherits from
          <select class="input" bind:value={draft.parent}><option value="">Nothing</option>
            {#each supertags.list.filter((t) => t.name !== selected.name) as t (t.id)}<option value={t.name}>#{t.name}</option>{/each}</select></label>
        <label>Colour <input class="input color" type="color" bind:value={draft.color} /></label>
        <label>Layout
          <select class="input" bind:value={draft.layout}><option value="">Default</option><option value="table">Table</option><option value="kanban">Board</option><option value="gallery">Gallery</option><option value="list">List</option></select></label>
        <label>Template
          <select class="input" bind:value={draft.template}><option value="">None</option>{#each templatesStore.list as t (t.id)}<option value={t.id}>{t.title}</option>{/each}</select></label>
        <label class="check"><input type="checkbox" bind:checked={draft.autoApply} disabled={!draft.template} /> Apply the template when this tag is added</label>
        <label class="wide">Description <input class="input" bind:value={draft.description} /></label>
        <label class="wide">Rename
          <span class="inline"><input class="input" bind:value={renameTo} /><button class="btn" onclick={rename} disabled={renameTo.trim() === selected.name}>Rename everywhere</button></span></label>
      </div>

      <h2 class="label">Fields</h2>
      {#each selected.allFields.filter((f) => f.inherited) as f (f.key)}
        <div class="field inherited"><span>{f.name}</span><span class="faint">{f.type}</span><span class="faint">from #{f.inherited}</span><code class="faint">{f.key}</code></div>
      {/each}
      {#each draft.fields as f, i (i)}
        <div class="field">
          <input class="input name" bind:value={f.name} placeholder="Field name" title={selected.fields[i] ? `Stored as “${selected.allFields.find((x) => x.name === selected.fields[i].name)?.key}” in each note` : ''} />
          <select class="input type" aria-label="Field type" bind:value={f.type}>{#each types as t (t)}<option value={t}>{t}</option>{/each}</select>
          {#if f.type === 'select' || f.type === 'multiselect' || f.type === 'status'}<input class="input opts" bind:value={f.options} placeholder="Options, comma separated" />{:else}<span class="opts"></span>{/if}
          <label class="check"><input type="checkbox" bind:checked={f.required} /> required</label>
          {#if selected.fields[i]}
            <button class="btn ghost" title="Convert existing values" onclick={() => (migrate = { field: selected.allFields.find((x) => x.name === selected.fields[i].name)?.key ?? '', toType: f.type, toKey: '' })}>Migrate…</button>
          {/if}
          <button class="icon-btn" aria-label="Remove field" onclick={() => (draft!.fields = draft!.fields.filter((_, j) => j !== i))}><Icon name="x" size={13} /></button>
        </div>
      {/each}
      <button class="btn ghost" onclick={() => draft!.fields.push({ name: '', type: 'text', options: '', required: false, description: '' })}><Icon name="plus" size={13} /> Add field</button>
      <p class="faint small">Changing a field's type never rewrites your notes. Use Migrate to convert existing values when you're ready; values that don't convert are kept.</p>

      {#if migrate}
        <div class="migrate">
          <strong>Migrate “{migrate.field}”</strong>
          <label>Convert to <select class="input" bind:value={migrate.toType}><option value="">Keep type</option>{#each types as t (t)}<option value={t}>{t}</option>{/each}</select></label>
          <label>Move to key <input class="input" bind:value={migrate.toKey} placeholder="(same key)" /></label>
          <button class="btn primary" onclick={runMigration}>Migrate values</button>
          <button class="btn" onclick={() => (migrate = null)}>Cancel</button>
        </div>
      {/if}

      <h2 class="label">Tagged notes</h2>
      {#key selected.name}
        <QueryView tab={{ ...tab, arg: 'new' }} presetTags={[selected.name]} presetLayout={(selected.layout || null) as never} embedded />
      {/key}
      {#if formOpen}<FormDialog tag={selected} onclose={() => (formOpen = false)} />{/if}
    {:else}
      <div class="empty-state"><h3>Choose a supertag</h3><p>Supertags turn notes into typed records with fields, templates and their own views.</p></div>
    {/if}
  </section>
</div>

<style>
  .tags { flex: 1; display: grid; grid-template-columns: 270px 1fr; min-height: 0; }
  .list { border-right: 1px solid var(--line); padding: 12px 8px; }
  .new { display: grid; grid-template-columns: 1fr auto; gap: 4px; margin-bottom: 10px; }
  .new .input { height: 30px; }
  .new select { grid-column: 1; }
  .item { display: flex; align-items: center; gap: 8px; width: 100%; text-align: left; border: 0; background: none; color: var(--text-soft); padding: 6px 10px; border-radius: 6px; }
  .item:hover { background: var(--bg-hover); }
  .item.on { background: var(--bg-active); color: var(--text); }
  .swatch { width: 9px; height: 9px; border-radius: 3px; flex: none; }
  .count { margin-left: auto; font: 11px var(--font-mono); }
  .scope { font: 10.5px var(--font-mono); }
  .pad { padding: 8px; font-size: 13px; }
  .detail { padding: 20px 26px 60px; }
  header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  .tools { display: flex; gap: 6px; }
  .warn { color: var(--warn); display: flex; gap: 6px; align-items: center; }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 10px 14px; margin: 16px 0; }
  .grid label { display: grid; gap: 4px; font-size: 12.5px; color: var(--text-muted); }
  .grid .wide { grid-column: 1 / -1; }
  .grid .input { height: 30px; }
  .grid .color { width: 56px; padding: 2px; }
  .inline { display: flex; gap: 6px; }
  .check { display: flex !important; grid-auto-flow: column; gap: 6px; align-items: center; color: var(--text-soft); font-size: 13px; }
  h2 { margin: 22px 0 8px; }
  .field { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; margin-bottom: 6px; }
  .field .input { height: 30px; min-width: 0; }
  .field .name { width: 190px; flex: none; }
  .field .type { width: 125px; flex: none; }
  /* The options box wraps to its own line rather than shrinking to nothing in a narrow pane. */
  .field input.opts { flex: 1 1 220px; min-width: 0; }
  .field span.opts { flex: 1 1 0; }
  .field .check { white-space: nowrap; }
  .field .btn { flex: none; }
  .field.inherited { color: var(--text-soft); font-size: 13.5px; gap: 14px; padding: 4px 2px; }
  .small { font-size: 12.5px; }
  .migrate { display: flex; gap: 10px; align-items: end; flex-wrap: wrap; padding: 12px; border: 1px solid var(--line-strong); border-radius: 8px; background: var(--bg-raised); margin: 10px 0; }
  .migrate label { display: grid; gap: 4px; font-size: 12px; color: var(--text-muted); }
  .migrate .input { height: 30px; width: auto; }
  @container pane (max-width: 760px) {
    .tags { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
    .list { border-right: 0; border-bottom: 1px solid var(--line); max-height: 220px; }
  }
</style>
