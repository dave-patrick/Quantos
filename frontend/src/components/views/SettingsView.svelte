<script lang="ts">
  import { app } from '../../lib/state.svelte';
  import { prefs } from '../../lib/prefs.svelte';
  import { isWindowsClient } from '../../lib/platform';
  import Setup from '../Setup.svelte';
  import ShortcutSettings from '../ShortcutSettings.svelte';
  import ToolbarSettings from '../ToolbarSettings.svelte';
  import PluginSettings from '../PluginSettings.svelte';
  import { vaultSettings } from '../../lib/vaultSettings.svelte';
  import { templatesStore } from '../../lib/templates.svelte';
  import { onMount } from 'svelte';

  onMount(() => { vaultSettings.load(); templatesStore.load(); });

  let section = $state<'general' | 'editor' | 'vault' | 'shortcuts' | 'toolbar' | 'plugins'>('general');
  let switching = $state(false);
</script>

<div class="settings">
  <nav>
    {#each [['general', 'Appearance'], ['editor', 'Writing'], ['vault', 'Vault'], ['shortcuts', 'Shortcuts'], ['toolbar', 'Toolbar'], ['plugins', 'Plugins & permissions']] as [key, label] (key)}
      <button class:on={section === key} onclick={() => (section = key as typeof section)}>{label}</button>
    {/each}
  </nav>
  <div class="scroll body">
    {#if section === 'general'}
      <h2>Appearance</h2>
      <div class="field"><span>Theme</span>
        <div class="seg"><button class:on={app.theme === 'dark'} onclick={() => (app.theme = 'dark')}>Dark</button><button class:on={app.theme === 'light'} onclick={() => (app.theme = 'light')}>Light</button></div></div>
      <div class="field"><span>Editor text size</span><input type="range" min="14" max="22" bind:value={prefs.editorSize} /> <span class="mono">{prefs.editorSize}px</span></div>
      <div class="field"><span>Typeface</span>
        <select class="input narrow" bind:value={prefs.fontFamily}><option value="sans">Source Sans 3</option><option value="serif">Serif</option><option value="mono">IBM Plex Mono</option><option value="system">System</option></select></div>
      <div class="field"><span>Line spacing</span><input type="range" min="1.3" max="2" step="0.05" bind:value={prefs.lineHeight} /> <span class="mono">{prefs.lineHeight}</span></div>
      <div class="field"><span>Line width</span><input type="range" min="560" max="1100" step="20" bind:value={prefs.editorWidth} /> <span class="mono">{prefs.editorWidth}px</span></div>
    {:else if section === 'editor'}
      <h2>Writing</h2>
      <label class="check"><input type="checkbox" bind:checked={prefs.typewriter} /> Typewriter scrolling (keep the current line centred)</label>
      <label class="check"><input type="checkbox" bind:checked={prefs.showWordCount} /> Show word count, characters and reading time</label>
      <p class="faint">Set a writing target for a note from its ⋯ menu.</p>
    {:else if section === 'vault'}
      <h2>Vault</h2>
      <p class="mono">{app.status?.vault}</p>
      {#if switching}
        <Setup switching onopened={() => location.reload()} />
      {:else}
        <button class="btn" onclick={() => (switching = true)}>Open another vault…</button>
      {/if}
      <h3>Notes and tasks</h3>
      <div class="field"><span>Daily note template</span>
        <select class="input narrow" value={vaultSettings.data.dailyTemplate ?? ''} onchange={(e) => vaultSettings.set('dailyTemplate', (e.target as HTMLSelectElement).value)}>
          <option value="">None</option>
          {#each templatesStore.list as t (t.id)}<option value={t.id}>{t.title}</option>{/each}
        </select></div>
      <label class="check"><input type="checkbox" checked={vaultSettings.data.inlineTasks !== false}
        onchange={(e) => vaultSettings.set('inlineTasks', (e.target as HTMLInputElement).checked ? null : false)} />
        Turn “- [ ] …” lines in notes into tasks automatically</label>
      <h3>Backups</h3>
      <p class="muted">Until configurable backups arrive in Release 1.1: {isWindowsClient ? 'use Exit in this window' : 'use Exit in the Windows client'} so the backend shuts down safely, then copy the whole Vault folder somewhere safe. You don't need the cache in client-data; it's rebuilt from the vault.</p>
    {:else if section === 'shortcuts'}
      <ShortcutSettings />
    {:else if section === 'toolbar'}
      <ToolbarSettings />
    {:else}
      <PluginSettings />
    {/if}
  </div>
</div>

<style>
  .settings { flex: 1; display: grid; grid-template-columns: 210px 1fr; min-height: 0; }
  nav { border-right: 1px solid var(--line); padding: 16px 8px; display: grid; align-content: start; gap: 2px; }
  nav button { text-align: left; border: 0; background: none; color: var(--text-soft); padding: 8px 12px; border-radius: 7px; }
  nav button:hover { background: var(--bg-hover); }
  nav button.on { background: var(--bg-active); color: var(--accent-strong); }
  .body { padding: 24px 32px 80px; max-width: 860px; }
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 18px; }
  h3 { font: 700 16px/1.3 var(--font-display); margin: 26px 0 6px; }
  .field { display: flex; align-items: center; gap: 14px; margin: 12px 0; }
  .field > span:first-child { width: 160px; color: var(--text-soft); }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 8px; overflow: hidden; }
  .seg button { border: 0; background: none; padding: 6px 14px; color: var(--text-muted); }
  .seg button.on { background: var(--accent-wash); color: var(--accent-strong); }
  .check { display: flex; gap: 8px; align-items: center; margin: 10px 0; color: var(--text-soft); }
  .narrow { width: auto; }
  .mono { font-family: var(--font-mono); font-size: 13px; color: var(--text-muted); }
</style>
