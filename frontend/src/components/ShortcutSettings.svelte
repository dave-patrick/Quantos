<script lang="ts">
  import { commandList, displayKeys, keysFor, normalizeKey, shortcutConflicts, shortcutOverrides } from '../lib/commands.svelte';
  import { saveShortcuts } from '../lib/customize.svelte';
  import { bridge, isWindowsClient } from '../lib/platform';
  import { app } from '../lib/state.svelte';
  import { onMount } from 'svelte';

  let recording = $state<string | null>(null);
  let filter = $state('');
  let capture = $state('Ctrl+Shift+Space');
  let scratch = $state('Ctrl+Shift+J');
  let hotkeyProblems = $state<string[]>([]);

  onMount(() => bridge.on((m) => {
    if (m?.type === 'client') { capture = m.captureHotkey; scratch = m.scratchpadHotkey; }
    if (m?.type === 'hotkey-status') hotkeyProblems = m.problems ?? [];
  }));

  const conflicts = $derived.by(() => { void shortcutOverrides.map; return shortcutConflicts(); });
  const conflictIds = $derived(new Set([...conflicts.values()].flat()));
  const commands = $derived(commandList.items.filter((c) => !filter.trim() || c.title.toLowerCase().includes(filter.trim().toLowerCase())).sort((a, b) => a.group.localeCompare(b.group) || a.title.localeCompare(b.title)));

  function onkey(e: KeyboardEvent, id: string) {
    e.preventDefault();
    e.stopPropagation();
    if (e.key === 'Escape') { recording = null; return; }
    const combo = normalizeKey(e);
    if (!combo || ['Mod', 'Shift', 'Alt', 'Mod+Shift', 'Mod+Alt'].includes(combo)) return;
    recording = null;
    saveShortcuts({ ...shortcutOverrides.map, [id]: combo }).catch((err) => app.error(err));
  }

  function reset(id: string) {
    const next = { ...shortcutOverrides.map };
    delete next[id];
    saveShortcuts(next).catch((err) => app.error(err));
  }

  function unbind(id: string) { saveShortcuts({ ...shortcutOverrides.map, [id]: '' }).catch((err) => app.error(err)); }
</script>

<h2>Shortcuts</h2>
{#if isWindowsClient}
  <section class="global">
    <h3>Global shortcuts <span class="faint">work in any app</span></h3>
    <label>Quick capture <input class="input" bind:value={capture} /></label>
    <label>Scratchpad <input class="input" bind:value={scratch} /></label>
    <button class="btn" onclick={() => bridge.send({ type: 'hotkeys', capture, scratchpad: scratch })}>Apply</button>
    {#each hotkeyProblems as p (p)}<p class="warn">{p}</p>{/each}
  </section>
{:else}
  <p class="muted">Global capture and scratchpad shortcuts <span class="requires-windows">Requires Windows client</span></p>
{/if}

<h3>In-app shortcuts</h3>
{#if conflicts.size}<p class="warn">{conflicts.size} shortcut{conflicts.size === 1 ? ' is' : 's are'} used by more than one command. The first match wins; change one of them.</p>{/if}
<input class="input filter" placeholder="Filter commands" bind:value={filter} />
<table>
  <tbody>
    {#each commands as c (c.id)}
      <tr class:conflict={conflictIds.has(c.id)}>
        <td class="group faint">{c.group}</td>
        <td>{c.title}</td>
        <td class="keys">
          {#if recording === c.id}
            <!-- svelte-ignore a11y_autofocus -->
            <input class="input rec" placeholder="Press keys… (Esc cancels)" onkeydown={(e) => onkey(e, c.id)} onblur={() => (recording = null)} autofocus readonly />
          {:else}
            <button class="key" onclick={() => (recording = c.id)} title="Click and press a new shortcut">{displayKeys(keysFor(c.id)) || '—'}</button>
          {/if}
        </td>
        <td class="actions">
          {#if shortcutOverrides.map[c.id] !== undefined}<button class="btn ghost" onclick={() => reset(c.id)}>Reset</button>{/if}
          {#if keysFor(c.id)}<button class="btn ghost" onclick={() => unbind(c.id)}>Remove</button>{/if}
        </td>
      </tr>
    {/each}
  </tbody>
</table>

<style>
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 14px; }
  h3 { font: 700 16px/1.3 var(--font-display); margin: 22px 0 8px; }
  .global { display: grid; gap: 8px; max-width: 420px; }
  .global label { display: grid; gap: 4px; font-size: 13px; color: var(--text-muted); }
  .warn { color: var(--warn); font-size: 13.5px; }
  .filter { max-width: 320px; margin-bottom: 8px; }
  table { width: 100%; border-collapse: collapse; font-size: 14px; }
  td { padding: 4px 6px; border-bottom: 1px solid var(--line); }
  .group { font: 11px var(--font-mono); width: 90px; }
  .keys { width: 200px; }
  .key { border: 1px solid var(--line-strong); border-bottom-width: 2px; background: var(--bg-raised); border-radius: 5px; padding: 2px 10px; font: 12px var(--font-mono); color: var(--text-soft); min-width: 60px; }
  .rec { height: 28px; font-size: 12px; }
  tr.conflict .key { border-color: var(--warn); color: var(--warn); }
  .actions { width: 160px; text-align: right; }
  .actions .btn { height: 24px; font-size: 12px; padding: 0 8px; }
</style>
