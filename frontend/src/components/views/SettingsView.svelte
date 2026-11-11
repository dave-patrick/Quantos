<script lang="ts">
  import ThemeEditor from '../ThemeEditor.svelte';
  import { app } from '../../lib/state.svelte';
  import { prefs } from '../../lib/prefs.svelte';
  import { isWindowsClient, isAndroidClient } from '../../lib/platform';
  import Setup from '../Setup.svelte';
  import ShortcutSettings from '../ShortcutSettings.svelte';
  import ToolbarSettings from '../ToolbarSettings.svelte';
  import PluginSettings from '../PluginSettings.svelte';
  import AiSettingsPanel from '../AiSettings.svelte';
  import BackupSettings from '../BackupSettings.svelte';
  import UpdateSettings from '../UpdateSettings.svelte';
  import McpSettings from '../McpSettings.svelte';
  import SyncSettings from '../SyncSettings.svelte';
  import GoogleTasksSettings from '../GoogleTasksSettings.svelte';
  import PhoneNavigationSettings from '../PhoneNavigationSettings.svelte';
  import { vaultSettings } from '../../lib/vaultSettings.svelte';
  import { templatesStore } from '../../lib/templates.svelte';
  import { onMount } from 'svelte';
  import { themes, type Density } from '../../lib/appearance.svelte';

  onMount(() => { vaultSettings.load(); templatesStore.load(); themes.list(); });

  import type { Tab } from '../../lib/layout.svelte';
  let { tab }: { tab?: Tab } = $props();
  type Section = 'general' | 'editor' | 'vault' | 'shortcuts' | 'toolbar' | 'plugins' | 'ai' | 'agents' | 'backups' | 'updates' | 'sync' | 'google-tasks' | 'phone-navigation';
  let section = $state<Section>('general');
  let navSide = $state<'left' | 'right' | 'top' | 'bottom'>('left');
  // A link can open a particular section ("Open AI settings").
  $effect(() => { if (tab?.arg) section = tab.arg as Section; });
  let switching = $state(false);
  let tagScopeBusy = $state(false);
  async function setTagDefault(value: string) {
    const previous = vaultSettings.data.defaultTagScope;
    tagScopeBusy = true;
    try { await vaultSettings.set('defaultTagScope', value); app.toast('Default tag scope saved.', 'ok'); }
    catch (e) { vaultSettings.data.defaultTagScope = previous; app.error(e); }
    finally { tagScopeBusy = false; }
  }

  /** Vault/settings.json "historyKeep": versions kept per note (default 10, 0 = all). */
  const historyKeep = $derived(typeof vaultSettings.data.historyKeep === 'number' ? vaultSettings.data.historyKeep : 10);
  async function setHistoryKeep(input: HTMLInputElement) {
    const next = Math.floor(Number(input.value));
    if (!Number.isFinite(next) || next < 0 || next > 10000) { input.value = String(historyKeep); app.toast('Enter a number from 0 to 10,000.', 'error'); return; }
    if (next === historyKeep) return;
    const fewer = next !== 0 && (historyKeep === 0 || next < historyKeep);
    if (fewer && !(await app.confirm(`Keep only ${next} ${next === 1 ? 'version' : 'versions'} of each note?`, 'Older versions are deleted from every note’s history. This can’t be undone.', 'Delete older versions', 'danger'))) {
      input.value = String(historyKeep); return;
    }
    try { await vaultSettings.set('historyKeep', next === 10 ? null : next); app.toast('Version history setting saved.', 'ok'); }
    catch (e) { input.value = String(historyKeep); app.error(e); }
  }
</script>

<div class="settings" class:nav-left={navSide === 'left'} class:nav-right={navSide === 'right'} class:nav-top={navSide === 'top'} class:nav-bottom={navSide === 'bottom'}>
  <h1 class="visually-hidden">Settings</h1>
  <label class="phone-settings-select">Settings section
    <select class="input" bind:value={section}>
      <option value="general">Appearance</option><option value="editor">Writing</option><option value="vault">Vault</option><option value="sync">Shared vault</option><option value="google-tasks">Google Tasks</option>
      <option value="shortcuts">Shortcuts</option><option value="toolbar">Toolbar</option><option value="plugins">Plugins & permissions</option><option value="ai">AI</option>
      {#if isAndroidClient}<option value="phone-navigation">Phone navigation</option>{/if}
      <option value="agents">MCP</option><option value="backups">Backups</option><option value="updates">Updates</option>
    </select>
  </label>
  <nav>
    {#if isAndroidClient}<button class:on={section === 'phone-navigation'} onclick={() => section = 'phone-navigation'}>Phone navigation</button>{/if}
    {#each [['general', 'Appearance'], ['editor', 'Writing'], ['vault', 'Vault'], ['sync', 'Shared vault'], ['google-tasks', 'Google Tasks'], ['shortcuts', 'Shortcuts'], ['toolbar', 'Toolbar'], ['plugins', 'Plugins & permissions'], ['ai', 'AI'], ['agents', 'MCP'], ['backups', 'Backups'], ['updates', 'Updates']] as [key, label] (key)}
      <button class:on={section === key} onclick={() => (section = key as typeof section)}>{label}</button>
    {/each}
  </nav>
  <div class="scroll body">
    {#if section === 'general'}
      <h2>Appearance</h2>
      <ThemeEditor />
      <h3 class="workspace-settings-nav">Settings navigation</h3>
      <div class="field workspace-settings-nav"><span>Nav position</span>
        <div class="seg">{#each [['left', 'Left'], ['top', 'Top'], ['right', 'Right'], ['bottom', 'Bottom']] as [key, label] (key)}<button class:on={navSide === key} onclick={() => (navSide = key as any)}>{label}</button>{/each}</div></div>
      <h3>Writing look</h3>
      <div class="field"><span>Editor text size</span><input type="range" aria-label="Editor text size" min="14" max="22" bind:value={prefs.editorSize} /> <span class="mono">{prefs.editorSize}px</span></div>
      <div class="field"><span>Editor typeface</span>
        <select class="input narrow" aria-label="Typeface" bind:value={prefs.fontFamily}><option value="sans">Source Sans 3</option><option value="serif">Serif</option><option value="mono">IBM Plex Mono</option><option value="system">System</option></select></div>
      <div class="field"><span>Line spacing</span><input type="range" aria-label="Line spacing" min="1.3" max="2" step="0.05" bind:value={prefs.lineHeight} /> <span class="mono">{prefs.lineHeight}</span></div>
      <div class="field"><span>Line width</span><input type="range" aria-label="Line width" min="560" max="1100" step="20" bind:value={prefs.editorWidth} /> <span class="mono">{prefs.editorWidth}px</span></div>
      <div class="field"><span>Density</span>
        <div class="seg">{#each [['compact', 'Compact'], ['comfortable', 'Comfortable'], ['spacious', 'Spacious']] as [key, label] (key)}<button class:on={prefs.density === key} onclick={() => (prefs.density = key as Density)}>{label}</button>{/each}</div></div>
      <p class="faint note">Theme, colours, fonts and density apply to this device. Custom styles below are part of the vault and can be applied anywhere.</p>

      <h3>Custom styles</h3>
      <p class="faint note">Put <code>.css</code> files in the vault's <code>themes</code> folder, then apply one here. They load after Quantos's own styles, so they can change any colour or size (the design tokens are CSS variables such as <code>--accent</code> and <code>--bg</code>). They can't load anything from the internet. Safe mode turns them off.</p>
      {#if themes.files.length || themes.active}
        <div class="styles" role="radiogroup" aria-label="Custom style">
          <button role="radio" aria-checked={!themes.active} class:on={!themes.active} onclick={() => themes.use(null)}>None</button>
          {#each themes.files as f (f.name)}
            <button role="radio" aria-checked={themes.active === f.name} class:on={themes.active === f.name} onclick={() => themes.use(f.name)}>
              {f.name} <span class="faint">{(f.size / 1024).toFixed(1)} KB</span>
            </button>
          {/each}
        </div>
      {:else}
        <p class="faint">No styles yet.</p>
      {/if}
      {#if themes.error}<p class="warn">{themes.error}</p>{/if}
      <button class="btn ghost" onclick={async () => { await themes.list(); await themes.apply(); app.toast('Styles reloaded.', 'ok'); }}>Reload styles</button>
    {:else if section === 'phone-navigation' && isAndroidClient}
      <PhoneNavigationSettings />
    {:else if section === 'editor'}
      <h2>Writing</h2>
      <label class="check"><input type="checkbox" bind:checked={prefs.typewriter} /> Typewriter scrolling (keep the current line centred)</label>
      <label class="check"><input type="checkbox" bind:checked={prefs.showWordCount} /> Show word count, characters and reading time</label>
      <label class="check"><input type="checkbox" bind:checked={prefs.collapsibleHeadings} /> Collapsible headings</label>
      <label class="check"><input type="checkbox" bind:checked={prefs.spellCheck} /> Spell check (underline unknown words)</label>
      <p class="faint">Checks English spelling with bundled US and UK dictionaries. Works offline; code and web addresses are skipped.</p>
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
        <select class="input narrow" aria-label="Daily note template" value={vaultSettings.data.dailyTemplate ?? ''} onchange={(e) => vaultSettings.set('dailyTemplate', (e.target as HTMLSelectElement).value)}>
          <option value="">None</option>
          {#each templatesStore.list as t (t.id)}<option value={t.id}>{t.title}</option>{/each}
        </select></div>
      <label class="check"><input type="checkbox" checked={vaultSettings.data.inlineTasks !== false}
        onchange={(e) => vaultSettings.set('inlineTasks', (e.target as HTMLInputElement).checked ? null : false)} />
        Turn “- [ ] …” lines in notes into tasks automatically</label>
      <h3>Tags</h3>
      <label class="field">Default tag scope
        <select class="input" aria-label="Default tag scope" value={vaultSettings.data.defaultTagScope === 'shared' ? 'shared' : 'workspace'} disabled={tagScopeBusy}
          onchange={(e) => setTagDefault(e.currentTarget.value)}>
          <option value="workspace">Workspace-specific</option><option value="shared">Shared across workspaces</option>
        </select>
      </label>
      <p class="muted">Applies to new tags created in Supertags or added through note properties. Each tag can override this in Supertags. Imported tags and inline hashtags keep their existing values. Linked folders use shared definitions.</p>
      <h3>Version history</h3>
      <div class="field"><span>Versions to keep for each note</span>
        <input class="input narrow" type="number" min="0" max="10000" step="1" aria-label="Versions to keep for each note"
          value={historyKeep} onchange={(e) => setHistoryKeep(e.currentTarget)} /></div>
      <p class="muted">Older versions are deleted. 0 keeps every version.</p>
      <h3>Backups</h3>
      <p class="muted">{isAndroidClient ? 'Use Settings › Shared vault › Android files › Export vault to keep a portable copy outside the app.' : 'Use Settings › Backups to configure rolling backups, make a backup now, or verify and restore a saved copy.'}</p>
    {:else if section === 'sync'}
      <SyncSettings />
    {:else if section === 'google-tasks'}
      <GoogleTasksSettings />
    {:else if section === 'shortcuts'}
      <ShortcutSettings />
    {:else if section === 'toolbar'}
      <ToolbarSettings />
    {:else if section === 'plugins'}
      <PluginSettings />
    {:else if section === 'ai'}
      <AiSettingsPanel />
    {:else if section === 'agents'}
      <McpSettings />
    {:else if section === 'backups'}
      <BackupSettings />
    {:else if section === 'updates'}
      <UpdateSettings />
    {/if}
  </div>
</div>

<style>
  .phone-settings-select { display: none; }
  .settings { flex: 1; display: grid; grid-template-columns: 210px 1fr; min-height: 0; gap: 0; }

  .settings.nav-top { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
  .settings.nav-top nav { border-right: 0; border-bottom: 1px solid var(--line); display: flex; flex-wrap: wrap; gap: 2px; }
  .settings.nav-bottom { grid-template-columns: 1fr; grid-template-rows: 1fr auto; }
  .settings.nav-bottom nav { border-right: 0; border-top: 1px solid var(--line); order: 1; display: flex; flex-wrap: wrap; gap: 2px; }
  .settings.nav-right { grid-template-columns: 1fr 210px; }
  .settings.nav-right nav { border-right: 0; border-left: 1px solid var(--line); order: 1; }
  nav { border-right: 1px solid var(--line); padding: 16px 8px; display: grid; align-content: start; gap: 2px; }
  nav button { text-align: left; border: 0; background: none; color: var(--text-soft); padding: 8px 12px; border-radius: 7px; }
  nav button:hover { background: var(--bg-hover); }
  nav button.on { background: var(--bg-active); color: var(--accent-strong); }
  .body { padding: 16px 20px 24px; max-width: 860px; }
  .body > h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 18px; padding-bottom: 12px; border-bottom: 1px solid var(--line); }
  .body > h3 { font: 700 16px/1.3 var(--font-display); margin: 26px 0 6px; padding: 10px 14px; background: var(--bg-raised); border: 1px solid var(--line); border-radius: 8px; }
  .field { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 14px; margin: 12px 0; min-width: 0; }
  .field > span:first-child { width: 160px; max-width: 100%; color: var(--text-soft); }
  .field .input, .field input[type='range'] { min-width: 0; max-width: 100%; }
  .seg { display: inline-flex; flex-wrap: wrap; max-width: 100%; border: 1px solid var(--line-strong); border-radius: 8px; overflow: hidden; }
  .seg button { border: 0; background: none; padding: 6px 14px; color: var(--text-muted); }
  .seg button.on { background: var(--accent-wash); color: var(--accent-strong); }
  /* The custom styles: one applied at a time, so they can't fight each other. */
  .styles { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0; }
  .styles button { display: inline-flex; align-items: center; gap: 8px; padding: 7px 13px; border: 1px solid var(--line-strong); border-radius: 8px; background: var(--bg-raised); color: var(--text-soft); font: 600 13.5px var(--font-body); cursor: pointer; }
  .styles button:hover { background: var(--bg-hover); color: var(--text); }
  .styles button.on { background: var(--accent-wash); color: var(--accent-strong); border-color: var(--accent); }
  .note { font-size: 13px; max-width: 640px; }
  .check { display: flex; gap: 8px; align-items: center; margin: 10px 0; color: var(--text-soft); }
  .narrow { width: auto; }
  .mono { font-family: var(--font-mono); font-size: 13px; color: var(--text-muted); }
  @container pane (max-width: 760px) {
    .settings { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
    nav { display: flex; flex-wrap: wrap; border-right: 0; border-bottom: 1px solid var(--line); padding: 8px; }
    nav button { width: auto; padding: 6px 10px; }
    .body { padding: 12px 12px 24px; }
  }
</style>
