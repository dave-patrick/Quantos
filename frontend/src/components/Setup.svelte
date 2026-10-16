<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import type { VaultCheck } from '../lib/types';
  import Wordmark from './Wordmark.svelte';
  import Icon from './Icon.svelte';

  let { onopened, switching = false }: { onopened: () => void; switching?: boolean } = $props();

  let path = $state('');
  let recent = $state<string[]>([]);
  let check = $state<VaultCheck | null>(null);
  let acceptSynced = $state(false);
  let busy = $state(false);
  let error = $state('');
  let timer: ReturnType<typeof setTimeout>;

  onMount(async () => {
    const setup = await api.get<{ vault: string | null; defaultVault: string; recent: string[]; defaultCheck: VaultCheck }>('setup');
    recent = setup.recent;
    path = switching ? '' : setup.vault ?? setup.defaultVault;
    if (path) runCheck();
  });

  function onInput() {
    clearTimeout(timer);
    acceptSynced = false;
    timer = setTimeout(runCheck, 300);
  }

  async function runCheck() {
    if (!path.trim()) { check = null; return; }
    try { check = await api.post<VaultCheck>('setup/check', { path }); error = ''; }
    catch (e) { error = (e as Error).message; }
  }

  async function open(target = path) {
    busy = true;
    error = '';
    try {
      await api.post('setup/open', { path: target });
      onopened();
    } catch (e) {
      error = (e as Error).message;
    } finally {
      busy = false;
    }
  }

  const canOpen = $derived(!!check && !check.error && (!check.warning || acceptSynced) && !busy);
</script>

<main class="setup">
  <section class="panel">
    <Wordmark height={40} />
    <h1>{switching ? 'Open another vault' : 'Choose where your vault lives'}</h1>
    <p class="lead">
      Your vault is a folder of Markdown files. Quantos reads and writes it; you can open the files with any editor and copy the folder to back it up.
    </p>

    <label class="label" for="vault-path">Vault folder</label>
    <div class="row">
      <input id="vault-path" class="input mono" bind:value={path} oninput={onInput} spellcheck="false" placeholder="C:\Users\you\Quantos\Vault" />
    </div>

    {#if check}
      <div class="check" class:bad={!!check.error}>
        {#if check.error}
          <Icon name="alert" /> <span>{check.error}</span>
        {:else if check.looksLikeVault}
          <Icon name="check" /> <span>Existing vault found. Quantos will open it.</span>
        {:else if check.exists && !check.empty}
          <Icon name="info" /> <span>This folder already has files. Markdown files inside workspace folders will be adopted; nothing else is touched.</span>
        {:else}
          <Icon name="folder" /> <span>A new vault will be created here.</span>
        {/if}
      </div>
      {#if check.warning}
        <div class="warning" role="alert">
          <Icon name="alert" size={18} />
          <div>
            <strong>{check.syncedService} folder</strong>
            <p>{check.warning} Risks: files locked while saving, conflict copies, and corrupted files.</p>
            <label class="accept"><input type="checkbox" bind:checked={acceptSynced} /> I understand the risks and want to use this folder anyway</label>
          </div>
        </div>
      {/if}
    {/if}

    {#if error}<p class="error">{error}</p>{/if}

    <div class="actions">
      <button class="btn primary" disabled={!canOpen} onclick={() => open()}>
        {busy ? 'Opening…' : check?.looksLikeVault ? 'Open vault' : 'Create vault'}
      </button>
    </div>

    {#if recent.length}
      <h2 class="label">Recent vaults</h2>
      <ul class="recent">
        {#each recent as r (r)}
          <li><button class="link" onclick={() => { path = r; runCheck(); }}><Icon name="database" size={14} /> <span class="mono">{r}</span></button></li>
        {/each}
      </ul>
    {/if}
  </section>
</main>

<style>
  .setup { height: 100%; overflow: auto; display: grid; place-items: center; padding: 32px 16px;
    background: radial-gradient(1000px 520px at 85% -5%, #15263c 0%, transparent 60%), var(--bg); }
  .panel { width: min(640px, 100%); display: grid; gap: 14px; }
  h1 { font: 700 30px/1.2 var(--font-display); margin-top: 18px; }
  .lead { color: var(--text-soft); margin: 0 0 8px; max-width: 60ch; }
  .row { display: flex; gap: 8px; }
  .check { display: flex; gap: 8px; align-items: flex-start; color: var(--ok); font-size: 14px; }
  .check.bad { color: var(--danger); }
  .warning { display: flex; gap: 12px; padding: 14px 16px; border-radius: var(--radius); background: var(--warn-wash);
    border: 1px solid color-mix(in srgb, var(--warn) 45%, transparent); color: var(--warn); }
  .warning p { margin: 4px 0 10px; color: var(--text-soft); font-size: 14px; }
  .warning strong { color: var(--text); }
  .accept { display: flex; gap: 8px; align-items: center; color: var(--text); font-size: 14px; cursor: pointer; }
  .error { color: var(--danger); margin: 0; }
  .actions { margin-top: 6px; }
  .recent { list-style: none; margin: 0; padding: 0; display: grid; gap: 2px; }
  .link { display: flex; gap: 8px; align-items: center; background: none; border: 0; color: var(--text-soft); padding: 6px 8px; border-radius: 6px; width: 100%; text-align: left; font-size: 13px; }
  .link:hover { background: var(--bg-hover); color: var(--text); }
  h2.label { margin-top: 18px; }
</style>
