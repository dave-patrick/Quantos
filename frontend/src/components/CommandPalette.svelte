<script lang="ts">
  import { tick } from 'svelte';
  import { app } from '../lib/state.svelte';
  import { commandList, displayKeys, keysFor, newNote } from '../lib/commands.svelte';
  import { openNode, searchTitles } from '../lib/links.svelte';
  import type { Where } from '../lib/layout.svelte';
  import Icon from './Icon.svelte';
  import { recent } from '../lib/recent.svelte';

  let query = $state('');
  let index = $state(0);
  let input = $state<HTMLInputElement | null>(null);

  type Item = { key: string; label: string; detail?: string; icon: string; keys?: string; run: (where: Where) => void };

  // Where focus was before the palette opened (usually an editor), so typing can carry on after a command.
  let returnTo: HTMLElement | null = null;

  $effect(() => {
    if (app.paletteOpen) {
      returnTo = document.activeElement instanceof HTMLElement && document.activeElement !== document.body ? document.activeElement : null;
      query = app.paletteMode === 'commands' ? '>' : '';
      index = 0;
      tick().then(() => input?.focus());
    }
  });

  const commandMode = $derived(query.startsWith('>'));
  const items = $derived.by((): Item[] => {
    if (commandMode) {
      const q = query.slice(1).trim().toLowerCase();
      return commandList.items
        .filter((c) => !c.when || c.when())
        .filter((c) => !q || c.title.toLowerCase().includes(q) || c.group.toLowerCase().includes(q))
        .slice(0, 60)
        .map((c) => ({ key: c.id, label: c.title, detail: c.group, icon: c.icon ?? 'zap', keys: displayKeys(keysFor(c.id)), run: () => c.run() }));
    }
    const source = query.trim() ? searchTitles(query, 40) : recent.ids.map((id) => app.titleIndex.get(id)).filter((n) => !!n).slice(0, 15).concat(searchTitles('', 25).filter((n) => !recent.ids.includes(n.id))).slice(0, 40);
    const nodes = source.map((n): Item => ({
      key: n.id, label: n.title, detail: `${n.type}${n.workspace !== app.workspace ? ' · ' + n.workspace : ''}${n.archived ? ' · archived' : ''}`,
      icon: n.type === 'task' ? 'check-square' : n.type === 'daily' ? 'calendar' : n.type === 'person' ? 'user' : 'file-text',
      run: (where) => openNode(n.id, where, n.title)
    }));
    if (query.trim()) {
      nodes.push({ key: 'create', label: `Create note “${query.trim()}”`, icon: 'plus', run: (where) => newNote(where, query.trim()).catch((e) => app.error(e)) });
    }
    return nodes;
  });

  function close() {
    app.paletteOpen = false;
    const target = returnTo;
    returnTo = null;
    // Give focus back unless the command moved it somewhere (a new note's title, a dialog).
    tick().then(() => {
      const now = document.activeElement;
      if (target?.isConnected && (!now || now === document.body)) target.focus();
    });
  }

  function choose(item: Item | undefined, e?: KeyboardEvent | MouseEvent) {
    if (!item) return;
    close();
    const where: Where = e?.shiftKey ? 'other' : e && (e.ctrlKey || e.metaKey) ? 'new' : 'current';
    item.run(where);
  }

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') { close(); return; }
    if (e.key === 'ArrowDown') { e.preventDefault(); index = Math.min(index + 1, items.length - 1); }
    if (e.key === 'ArrowUp') { e.preventDefault(); index = Math.max(index - 1, 0); }
    if (e.key === 'Enter') { e.preventDefault(); choose(items[index], e); }
  }

  $effect(() => { void query; index = 0; });
</script>

{#if app.paletteOpen}
  <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
  <div class="scrim" onclick={close}>
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div class="palette" role="dialog" aria-label="Command palette" tabindex="-1" onclick={(e) => e.stopPropagation()}>
      <div class="field">
        <Icon name={commandMode ? 'command' : 'search'} size={16} />
        <input bind:this={input} bind:value={query} {onkeydown} placeholder={commandMode ? 'Run a command…' : 'Jump to a note, or type > for commands'} aria-label="Search" />
      </div>
      <ul role="listbox">
        {#each items as item, i (item.key)}
          <li role="option" aria-selected={i === index}>
            <button class:on={i === index} onmouseenter={() => (index = i)} onclick={(e) => choose(item, e)}>
              <Icon name={item.icon} size={15} />
              <span class="name">{item.label}</span>
              {#if item.detail}<span class="detail">{item.detail}</span>{/if}
              {#if item.keys}<kbd>{item.keys}</kbd>{/if}
            </button>
          </li>
        {:else}
          <li class="none">No matches</li>
        {/each}
      </ul>
      <div class="hint faint"><span><kbd>↵</kbd> open</span><span><kbd>Ctrl ↵</kbd> new tab</span><span><kbd>Shift ↵</kbd> other pane</span><span><kbd>&gt;</kbd> commands</span></div>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; background: rgb(4 8 14 / .55); z-index: 75; display: flex; justify-content: center; align-items: flex-start; padding: 12vh 16px 16px; }
  .palette { width: min(640px, 100%); background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 12px; box-shadow: var(--shadow); overflow: hidden; }
  .field { display: flex; align-items: center; gap: 10px; padding: 0 14px; border-bottom: 1px solid var(--line); color: var(--text-faint); }
  .field input { flex: 1; height: 50px; border: 0; background: transparent; font-size: 17px; }
  .field input:focus { outline: none; }
  ul { list-style: none; margin: 0; padding: 6px; max-height: 50vh; overflow: auto; }
  li button { display: flex; align-items: center; gap: 10px; width: 100%; padding: 8px 10px; border: 0; border-radius: 7px; background: none; text-align: left; color: var(--text-soft); }
  li button.on { background: var(--bg-select); color: var(--text); }
  .name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .detail { font: 11.5px var(--font-mono); color: var(--text-faint); }
  .none { padding: 14px; color: var(--text-faint); text-align: center; }
  .hint { display: flex; gap: 14px; padding: 8px 14px; border-top: 1px solid var(--line); font-size: 12px; }
</style>
