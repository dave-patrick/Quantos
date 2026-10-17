<script lang="ts">
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { switchWorkspace } from '../lib/workspace';
  import Icon from './Icon.svelte';
  import { menu } from '../lib/menu';

  let open = $state(false);
  let creating = $state(false);
  let name = $state('');

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
</script>

<div class="switcher">
  <button class="current" aria-haspopup="menu" aria-expanded={open} onclick={() => (open = !open)}>
    <span class="label">Workspace</span>
    <span class="name">{app.workspace}</span>
    <Icon name="chevron-down" size={14} />
  </button>
  {#if open}
    <button class="backdrop" aria-label="Close" onclick={() => (open = false)}></button>
    <div class="menu" role="menu" tabindex="-1" use:menu={{ onclose: () => { open = false; creating = false; } }}>
      {#each app.workspaces as ws (ws)}
        <button role="menuitemradio" aria-checked={ws === app.workspace} class:on={ws === app.workspace}
          onclick={() => { open = false; switchWorkspace(ws); }}>
          <Icon name={ws === app.workspace ? 'check' : 'layers'} size={14} /> {ws}
        </button>
      {/each}
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
  .menu > button.on { color: var(--accent); }
  hr { border: 0; border-top: 1px solid var(--line); margin: 4px 0; }
  .new { display: flex; gap: 6px; padding: 4px; }
</style>
