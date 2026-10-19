<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { openNode } from '../../lib/links.svelte';
  import { compare } from '../../lib/diff';
  import type { HistoryVersion, NodeDto } from '../../lib/types';
  import Icon from '../Icon.svelte';

  /** Version history with a detailed comparison (Release 1.1): any two versions, word by word. */
  let { id }: { id: string } = $props();
  let versions = $state<HistoryVersion[]>([]);
  let current = $state<NodeDto | null>(null);
  let selected = $state<string | null>(null);
  let texts = $state<Record<string, string>>({});
  let against = $state<'current' | 'previous' | 'none'>('current');
  let sideBySide = $state(false);
  let opened = $state<Record<number, boolean>>({});
  let at = $state(-1);
  let body = $state<HTMLElement | null>(null);

  const kinds: Record<string, string> = { save: 'Saved', before: 'Before change', external: 'Changed outside Quantos', restore: 'Restored', conflict: 'Kept from conflict' };

  async function load() {
    versions = await api.get<HistoryVersion[]>(`nodes/${id}/history`);
    current = await api.get<NodeDto>(`nodes/${id}`).catch(() => null);
    if (!current) against = 'previous';
    if (versions.length && !selected) await pick(versions[Math.min(1, versions.length - 1)].version);
  }

  async function text(version: string) {
    if (!(version in texts)) texts[version] = (await api.get<{ content: string }>(`nodes/${id}/history/${version}`)).content;
    return texts[version];
  }

  const index = $derived(versions.findIndex((v) => v.version === selected));
  const previous = $derived(index >= 0 ? versions[index + 1] : undefined);

  async function pick(version: string) {
    selected = version;
    opened = {}; at = -1;
    await text(version);
    const i = versions.findIndex((v) => v.version === version);
    if (versions[i + 1]) await text(versions[i + 1].version);
  }

  // What the selected version is compared with, oldest on the left.
  const pair = $derived.by(() => {
    if (!selected || !(selected in texts)) return null;
    if (against === 'current' && current) return { before: texts[selected], after: current.content, label: 'this version → now' };
    if (against === 'previous') {
      if (!previous) return { before: '', after: texts[selected], label: 'first version' };
      if (!(previous.version in texts)) return null;
      return { before: texts[previous.version], after: texts[selected], label: 'the version before → this one' };
    }
    return null;
  });
  const result = $derived(pair ? compare(pair.before, pair.after) : null);

  async function jump(step: number) {
    if (!result?.changes) return;
    at = (at + step + result.changes) % result.changes;
    await tick();
    body?.querySelector(`[data-change="${at}"]`)?.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }

  async function rollback() {
    if (!selected) return;
    const ok = await app.confirm('Restore this version?', 'The current version is kept in history, so you can come back to it.', 'Restore version');
    if (!ok) return;
    try {
      await api.post(`nodes/${id}/rollback`, { version: selected });
      app.toast('Version restored.', 'ok', { label: 'Open', run: () => openNode(id, 'current') });
      selected = null;
      await load();
    } catch (e) { app.error(e); }
  }

  const when = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  onMount(load);
</script>

<div class="history">
  <aside class="scroll list">
    <h2 class="label">Versions</h2>
    {#if !current}<p class="warn">The note isn't in the vault right now; you can still read its versions.</p>{/if}
    {#each versions as v (v.version)}
      <button class="ver" class:on={v.version === selected} aria-pressed={v.version === selected} onclick={() => pick(v.version)}>
        <span>{when(v.savedAt)}</span>
        <span class="faint">{kinds[v.kind] ?? v.kind} · {(v.size / 1024).toFixed(1)} KB</span>
      </button>
    {:else}
      <p class="faint">No history yet.</p>
    {/each}
  </aside>

  <section class="scroll preview" bind:this={body}>
    <div class="bar">
      <div class="seg" role="radiogroup" aria-label="Compare">
        <button role="radio" aria-checked={against === 'current'} class:on={against === 'current'} disabled={!current} onclick={() => (against = 'current')}>Since then</button>
        <button role="radio" aria-checked={against === 'previous'} class:on={against === 'previous'} onclick={() => (against = 'previous')}>Made in this version</button>
        <button role="radio" aria-checked={against === 'none'} class:on={against === 'none'} onclick={() => (against = 'none')}>Text only</button>
      </div>
      {#if result}
        <label class="check"><input type="checkbox" bind:checked={sideBySide} /> Side by side</label>
        <span class="nav">
          <button class="icon-btn" title="Previous change" aria-label="Previous change" disabled={!result.changes} onclick={() => jump(-1)}><Icon name="arrow-up" size={15} /></button>
          <button class="icon-btn" title="Next change" aria-label="Next change" disabled={!result.changes} onclick={() => jump(1)}><Icon name="arrow-down" size={15} /></button>
        </span>
      {/if}
      <button class="btn primary restore" disabled={!selected || !current || index === 0} onclick={rollback}>Restore this version</button>
    </div>

    {#if result && pair}
      <p class="summary faint">
        Comparing {pair.label}:
        {#if result.changes || result.props.length}
          <span class="plus">+{result.added}</span> <span class="minus">−{result.removed}</span> lines ·
          {result.wordsAdded} {result.wordsAdded === 1 ? 'word' : 'words'} added, {result.wordsRemoved} removed
          {#if result.props.length} · {result.props.length} {result.props.length === 1 ? 'property' : 'properties'} changed{/if}
        {:else}no differences.{/if}
      </p>

      {#if result.props.length}
        <table class="props">
          <thead><tr><th>Property</th><th>Before</th><th>After</th></tr></thead>
          <tbody>
            {#each result.props as p (p.key)}
              <tr><td>{p.key}</td><td class:gone={p.before !== null}>{p.before ?? '—'}</td><td class:came={p.after !== null}>{p.after ?? '—'}</td></tr>
            {/each}
          </tbody>
        </table>
      {/if}

      <div class="diff" class:sbs={sideBySide}>
        {#each result.blocks as block, b (b)}
          {#if block.kind === 'fold' && !opened[b]}
            <button class="fold" onclick={() => (opened[b] = true)}>⋯ {block.rows.length} unchanged {block.rows.length === 1 ? 'line' : 'lines'}</button>
          {:else}
            <div class="block" data-change={block.kind === 'rows' ? block.change : null} class:here={block.kind === 'rows' && block.change === at}>
              {#each block.rows as r, i (i)}
                {#if sideBySide}
                  <div class="row {r.kind}">
                    <span class="no">{r.oldNo ?? ''}</span>
                    <div class="cell old">{#if r.old}{#each r.old as s, k (k)}<span class={s.kind}>{s.text}</span>{/each}{/if}</div>
                    <span class="no">{r.newNo ?? ''}</span>
                    <div class="cell new">{#if r.new}{#each r.new as s, k (k)}<span class={s.kind}>{s.text}</span>{/each}{/if}</div>
                  </div>
                {:else if r.kind === 'same'}
                  <div class="row same"><span class="no">{r.newNo}</span><div class="cell">{r.new?.[0].text}</div></div>
                {:else}
                  {#if r.old}<div class="row del"><span class="no">−</span><div class="cell">{#each r.old as s, k (k)}<span class={s.kind}>{s.text}</span>{/each}</div></div>{/if}
                  {#if r.new}<div class="row add"><span class="no">+</span><div class="cell">{#each r.new as s, k (k)}<span class={s.kind}>{s.text}</span>{/each}</div></div>{/if}
                {/if}
              {/each}
            </div>
          {/if}
        {/each}
      </div>
    {:else if selected && selected in texts}
      <pre class="plain">{texts[selected]}</pre>
    {/if}
  </section>
</div>

<style>
  .history { flex: 1; display: grid; grid-template-columns: 280px 1fr; min-height: 0; }
  .list { border-right: 1px solid var(--line); padding: 14px 10px; }
  .list h2 { margin: 0 6px 8px; }
  .ver { display: grid; gap: 2px; width: 100%; text-align: left; border: 0; background: none; color: var(--text-soft); padding: 8px 10px; border-radius: 7px; font-size: 13.5px; }
  .ver:hover { background: var(--bg-hover); }
  .ver.on { background: var(--bg-active); color: var(--text); }
  .ver .faint { font-size: 12px; }
  .preview { padding: 0 20px 40px; }
  .bar { display: flex; flex-wrap: wrap; gap: 10px 14px; align-items: center; padding: 12px 0; position: sticky; top: 0; background: var(--bg); z-index: 1; }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 7px; overflow: hidden; }
  .seg button { border: 0; background: none; color: var(--text-muted); padding: 5px 10px; font: 13px var(--font-body); cursor: pointer; }
  .seg button + button { border-left: 1px solid var(--line-strong); }
  .seg button.on { background: var(--accent-wash); color: var(--accent); }
  .seg button:disabled { opacity: .4; cursor: default; }
  .check { display: flex; gap: 6px; align-items: center; font-size: 13px; color: var(--text-soft); }
  .nav { display: inline-flex; gap: 2px; }
  .restore { margin-left: auto; }
  .summary { font-size: 13px; margin: 0 0 10px; }
  .plus { color: var(--ok); font-family: var(--font-mono); }
  .minus { color: var(--danger); font-family: var(--font-mono); }
  .props { border-collapse: collapse; margin: 0 0 14px; font-size: 13px; width: 100%; }
  .props th { text-align: left; font-weight: 600; color: var(--text-muted); padding: 4px 8px; border-bottom: 1px solid var(--line); }
  .props td { padding: 4px 8px; border-bottom: 1px solid var(--line); white-space: pre-wrap; font-family: var(--font-mono); font-size: 12.5px; vertical-align: top; }
  .props td.gone { color: var(--danger); }
  .props td.came { color: var(--ok); }
  .diff { font: 13px/1.6 var(--font-mono); border: 1px solid var(--line); border-radius: var(--radius); overflow: hidden; }
  .block.here { outline: 2px solid var(--accent); outline-offset: -2px; }
  .row { display: grid; grid-template-columns: 42px 1fr; }
  .sbs .row { grid-template-columns: 42px 1fr 42px 1fr; }
  .no { text-align: right; padding: 0 8px; color: var(--text-faint); user-select: none; border-right: 1px solid var(--line); }
  .cell { white-space: pre-wrap; overflow-wrap: anywhere; padding: 0 8px; color: var(--text-soft); min-width: 0; }
  .sbs .cell.old { border-right: 1px solid var(--line); }
  .row.del .cell, .sbs .row.mod .cell.old, .sbs .row.del .cell.old { background: color-mix(in srgb, var(--danger) 10%, transparent); color: var(--text); }
  .row.add .cell, .sbs .row.mod .cell.new, .sbs .row.add .cell.new { background: color-mix(in srgb, var(--ok) 10%, transparent); color: var(--text); }
  .cell .del { background: color-mix(in srgb, var(--danger) 35%, transparent); border-radius: 2px; }
  .cell .add { background: color-mix(in srgb, var(--ok) 32%, transparent); border-radius: 2px; }
  .row.del .cell .del:only-child, .row.add .cell .add:only-child { background: none; }
  .fold { display: block; width: 100%; text-align: left; border: 0; border-block: 1px solid var(--line); background: var(--bg-raised); color: var(--text-faint); font: 12px var(--font-mono); padding: 3px 50px; cursor: pointer; }
  .fold:hover { color: var(--accent); }
  .plain { margin: 0; font: 13px/1.6 var(--font-mono); white-space: pre-wrap; overflow-wrap: anywhere; color: var(--text-soft); padding: 8px; }
  .warn { color: var(--warn); font-size: 13px; padding: 0 6px; }
  @container pane (max-width: 760px) {
    .history { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
    .list { border-right: 0; border-bottom: 1px solid var(--line); max-height: 200px; }
    .sbs .row { grid-template-columns: 0 1fr 0 1fr; }
    .sbs .no { display: none; }
  }
</style>
