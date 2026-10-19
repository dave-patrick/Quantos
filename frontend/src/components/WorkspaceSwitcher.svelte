<script lang="ts">
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { switchWorkspace } from '../lib/workspace';
  import Icon from './Icon.svelte';
  import { menu } from '../lib/menu';

  let open = $state(false);
  let creating = $state(false);
  let name = $state('');
  const current = $derived(app.linked.find((l) => l.name === app.workspace) ?? null);

  async function create() {
    const clean = name.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '');
    if (!clean) return;
    try {
      app.workspaces = await api.post<string[]>('workspaces', { name: clean });
      creating = false;
      name = '';
      open = false;
      await switchWorkspace(clean);
    } catch (e) { app.error(e); }
  }

  /** Links a folder of Markdown from elsewhere on this computer. Its files are read and written in place. */
  async function linkFolder() {
    open = false;
    const folderPath = await app.ask('Link a folder', {
      message: 'Quantos reads and writes the Markdown in this folder in place, so the other app and Quantos both work on the same files.',
      placeholder: 'D:\\Notes',
      confirmLabel: 'Next'
    });
    if (!folderPath) return;
    const path = folderPath.trim().replace(/[\\/]+$/, '');
    if (!path) return;
    let clean = (path.split(/[\\/]/).pop() ?? 'linked').toLowerCase().replace(/[^a-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '') || 'linked';
    const taken = (n: string) => app.workspaces.includes(n) || app.linked.some((l) => l.name === n);
    if (taken(clean)) {
      const chosen = await app.ask('Name for this folder', { placeholder: clean, confirmLabel: 'Link' });
      if (!chosen) return;
      clean = chosen.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '');
      if (!clean) return;
    }
    try {
      await api.post('linked-folders', { name: clean, path });
      await app.refreshStatus();
      await switchWorkspace(clean);
      app.toast(`“${clean}” is linked. Its notes are read and written in place.`, 'ok');
    } catch (e) { app.error(e); }
  }

  /** Stops watching a linked folder. The files stay where they are. */
  async function unlinkFolder() {
    open = false;
    if (!current) return;
    const ok = await app.confirm(`Unlink “${current.name}”?`, 'Quantos stops reading and writing the folder. The files stay where they are.', 'Unlink', 'danger');
    if (!ok) return;
    try {
      await api.del(`linked-folders/${encodeURIComponent(current.name)}`);
      await app.refreshStatus();
      await switchWorkspace('default');
      app.toast(`“${current.name}” is unlinked. Its files were left alone.`, 'ok');
    } catch (e) { app.error(e); }
  }
</script>

<div class="switcher">
  <button class="current" aria-haspopup="menu" aria-expanded={open} onclick={() => (open = !open)}>
    <span class="label">{current ? 'Linked folder' : 'Workspace'}</span>
    <span class="name">{app.workspace}</span>
    <Icon name="chevron-down" size={14} />
  </button>
  {#if open}
    <button class="backdrop" aria-label="Close" onclick={() => (open = false)}></button>
    <div class="menu" role="menu" tabindex="-1" use:menu={{ onclose: () => { open = false; creating = false; } }}>
      <div class="group" role="group" aria-label="Workspaces">
        {#each app.workspaces as ws (ws)}
          <button role="menuitemradio" aria-checked={ws === app.workspace} class:on={ws === app.workspace}
            onclick={() => { open = false; switchWorkspace(ws); }}>
            <Icon name={ws === app.workspace ? 'check' : 'layers'} size={14} /> {ws}
          </button>
        {/each}
      </div>
      {#if app.linked.length}
        <hr />
        <div class="group" role="group" aria-label="Linked folders">
          {#each app.linked as l (l.name)}
            <button role="menuitemradio" aria-checked={l.name === app.workspace} class:on={l.name === app.workspace}
              title={l.path} onclick={() => { open = false; switchWorkspace(l.name); }}>
              <Icon name={l.name === app.workspace ? 'check' : 'link'} size={14} /> {l.name}
              {#if !l.exists}<span class="faint">folder missing</span>{/if}
            </button>
          {/each}
        </div>
      {/if}
      <hr />
      {#if creating}
        <form class="new" onsubmit={(e) => { e.preventDefault(); create(); }}>
          <!-- svelte-ignore a11y_autofocus -->
          <input class="input" aria-label="New workspace name" bind:value={name} placeholder="work, personal…" autofocus />
          <button class="btn primary" type="submit">Add</button>
        </form>
      {:else}
        <button role="menuitem" onclick={() => (creating = true)}><Icon name="plus" size={14} /> New workspace</button>
      {/if}
      <button role="menuitem" onclick={linkFolder}><Icon name="link" size={14} /> Link a folder…</button>
      {#if current}
        <button role="menuitem" onclick={unlinkFolder}><Icon name="x" size={14} /> Unlink “{current.name}”</button>
      {/if}
    </div>
  {/if}
</div>

<style>
  .switcher { position: relative; }
  .current { display: flex; align-items: center; gap: 6px; height: 30px; padding: 0 8px; border: 1px solid transparent; border-radius: 7px; background: transparent; }
  .current:hover { background: var(--bg-hover); }
  .label { font: 600 10px/1 var(--font-mono); letter-spacing: .08em; text-transform: uppercase; color: var(--text-faint); }
  .name { font: 700 14px/1 var(--font-display); letter-spacing: .02em; color: var(--text); }
  .backdrop { position: fixed; inset: 0; background: transparent; border: 0; z-index: 40; cursor: default; }
  .menu { position: absolute; top: 36px; left: 0; z-index: 41; min-width: 230px; padding: 6px; border-radius: 9px; background: var(--bg-raised); border: 1px solid var(--line-strong); box-shadow: var(--shadow); display: grid; gap: 1px; }
  .menu > button { display: flex; align-items: center; gap: 8px; padding: 7px 10px; border: 0; border-radius: 6px; background: transparent; text-align: left; color: var(--text-soft); }
  .menu > button:hover { background: var(--bg-hover); color: var(--text); }
  .group { display: grid; gap: 1px; }
  .group > button { display: flex; align-items: center; gap: 8px; padding: 7px 10px; border: 0; border-radius: 6px; background: transparent; text-align: left; color: var(--text-soft); }
  .group > button:hover { background: var(--bg-hover); color: var(--text); }
  .group > button.on { color: var(--accent); }
  .faint { color: var(--text-faint); font-size: 11px; }
  hr { border: 0; border-top: 1px solid var(--line); margin: 4px 0; }
  .new { display: flex; gap: 6px; padding: 4px; }
</style>
