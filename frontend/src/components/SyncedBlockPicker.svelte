<script lang="ts">
  import { tick } from 'svelte';
  import { modal } from '../lib/modal';
  import { syncedBlockPicker, matchingBlocks } from '../lib/syncedBlockPicker.svelte';

  const request = $derived(syncedBlockPicker.request);
  let query = $state('');
  let active = $state(0);
  let field = $state<HTMLInputElement | null>(null);
  const matches = $derived(matchingBlocks(request?.blocks ?? [], query));
  $effect(() => {
    if (!request) return;
    query = ''; active = 0;
    tick().then(() => field?.focus());
  });
  $effect(() => {
    const id = matches[active]?.id;
    if (id) tick().then(() => document.getElementById(`sync-choice-${id}`)?.scrollIntoView?.({ block: 'nearest' }));
  });
  function choose() { if (matches[active]) request?.done(matches[active]); }
  function onkeydown(event: KeyboardEvent) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (matches.length) active = (active + (event.key === 'ArrowDown' ? 1 : -1) + matches.length) % matches.length;
    } else if (event.key === 'Enter') { event.preventDefault(); choose(); }
  }
</script>

{#if request}
  <div class="scrim">
    <div class="picker" role="dialog" aria-modal="true" aria-labelledby="sync-picker-title" use:modal={{ onclose: () => request?.done(null) }}>
      <h2 id="sync-picker-title">Insert synced block</h2>
      <p>Edits to any copy update every copy.</p>
      <input class="input" bind:this={field} bind:value={query} oninput={() => (active = 0)} {onkeydown}
        placeholder="Search blocks" aria-label="Search synced blocks" role="combobox" aria-expanded="true"
        aria-controls="sync-picker-matches" aria-autocomplete="list" aria-activedescendant={matches[active] ? `sync-choice-${matches[active].id}` : undefined} />
      <p class="count" role="status">{matches.length} of {request.blocks.length} blocks</p>
      <div id="sync-picker-matches" class="matches" role="listbox" aria-label="Synced blocks">
        {#each matches as block, i (block.id)}
          <button id="sync-choice-{block.id}" role="option" aria-selected={active === i} class:active={active === i} tabindex="-1"
            onmouseenter={() => (active = i)} onclick={() => request?.done(block)}>
            <span class="title">{block.title}</span>
            <span class="uses">{block.uses} {block.uses === 1 ? 'use' : 'uses'}</span>
            <span class="preview">{block.content}</span>
          </button>
        {:else}<p class="empty">No blocks match your search.</p>{/each}
      </div>
      <div class="actions">
        <button class="btn" onclick={() => request?.done(null)}>Cancel</button>
        <button class="btn primary" onclick={choose} disabled={!matches.length}>Insert</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; z-index: 90; background: rgb(4 8 14 / .6); display: grid; place-items: center; padding: 16px; }
  .picker { width: min(540px, 100%); max-height: calc(100dvh - 32px); display: flex; flex-direction: column; gap: 12px; background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 10px; padding: 20px; box-shadow: var(--shadow); }
  h2 { font: 700 18px/1.3 var(--font-display); }
  p, .uses { color: var(--text-soft); }
  .input { width: 100%; }
  .count, .uses { font-size: 12px; }
  .matches { overflow-y: auto; min-height: 0; max-height: 360px; }
  .matches button { display: grid; grid-template-columns: minmax(0, 1fr) auto; width: 100%; gap: 4px 12px; text-align: left; padding: 8px; border-radius: 6px; }
  .matches button.active { background: var(--accent-wash); }
  .title { overflow-wrap: anywhere; }
  .preview { grid-column: 1 / -1; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; color: var(--text-muted); font-size: 12px; }
  .empty { padding: 8px; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
