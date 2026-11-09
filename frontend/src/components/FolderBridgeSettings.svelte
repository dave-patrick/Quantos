<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  interface Mapping { id: string; folder: string; workspace: string; enabled: boolean; busy: boolean; lastSynced: string | null; error: string | null; counts: { uploaded: number; downloaded: number; conflicts: number } | null }
  interface Status { supported: boolean; connected: boolean; workspaces: string[]; mappings: Mapping[] }
  let status = $state<Status | null>(null);
  let folder = $state('');
  let workspace = $state('');
  let busy = $state(false);
  let error = $state('');
  let disposed = false;
  let sequence = 0;
  async function refresh() {
    const request = ++sequence;
    const result = await api.get<Status>('folder-bridges/');
    if (!disposed && request === sequence) status = result;
  }
  onMount(() => {
    refresh().catch(e => error = e.message);
    const timer = setInterval(() => { if (!busy) refresh().catch(() => {}); }, 5000);
    return () => { disposed = true; sequence++; clearInterval(timer); };
  });
  async function perform(action: () => Promise<unknown>) {
    busy = true; error = ''; sequence++;
    try { await action(); await refresh(); }
    catch (e) { error = (e as Error).message; }
    finally { busy = false; }
  }
  async function chooseFolder() {
    const selected = await app.ask('Link a folder to the shared vault', {
      folder: true, value: folder, confirmLabel: 'Next',
      message: 'Choose the original Markdown folder on this computer. The folder stays in place. No transfer starts until you save and start the link.'
    });
    if (selected) { folder = selected; workspace = ''; }
  }
  async function start(mapping: Mapping) {
    if (await app.confirm('Start folder sync?', `Back up both copies first. Markdown files in ${mapping.folder} and shared workspace “${mapping.workspace}” sync in both directions, including deletions. Stop any other bridge for this folder, including bridges on other computers. Conflicting versions are kept for review.`, 'Start sync'))
      await perform(() => api.post(`folder-bridges/${mapping.id}/start`));
  }
  async function remove(mapping: Mapping) {
    if (await app.confirm('Remove this folder link?', 'Transfers stop. The original folder, server workspace and recovery copies stay intact. Linking again creates fresh sync state and combines both copies again.', 'Remove link'))
      await perform(() => api.del(`folder-bridges/${mapping.id}`));
  }
</script>

<section aria-labelledby="folder-bridge-heading">
  <h3 id="folder-bridge-heading">Link an original folder</h3>
  <p>Keep a folder managed by Google Drive or another editor in sync with a shared workspace. Only Markdown files transfer; their relative paths stay the same.</p>
  {#if status && !status.supported}
    <p class="muted">Set up the folder link in Windows Quantos on the computer that has the original folder. This device uses the shared workspace normally.</p>
  {:else}
    <p class="muted">Runs while Quantos is open on this computer. Keep this computer and its cloud drive available. Other devices use the shared workspace.</p>
    <p class="muted">If this folder is already linked locally in Quantos, unlink that local link first, leaving its files intact. Then use the shared workspace so the same notes are not indexed twice. Existing external bridges must be stopped and migrated separately.</p>
    {#if !status?.connected}<p>Connect this vault to a shared server above before linking a folder.</p>{/if}
    <button class="btn" disabled={busy || !status?.connected} onclick={chooseFolder}>Link a folder…</button>
    {#if folder}
      <form onsubmit={e => { e.preventDefault(); perform(async () => { await api.post('folder-bridges/', { folder, workspace }); folder = ''; workspace = ''; }); }}>
        <p class="path">Original folder: {folder}</p>
        <label for="bridge-workspace">Shared workspace</label>
        <input id="bridge-workspace" class="input" list="bridge-workspaces" bind:value={workspace} required pattern={'[a-z0-9][a-z0-9_-]{0,63}'} placeholder="Choose or name a workspace" autocapitalize="none" spellcheck="false" />
        <datalist id="bridge-workspaces">{#each status?.workspaces ?? [] as name}<option value={name}></option>{/each}</datalist>
        <p class="muted">Use lowercase letters, digits, hyphens or underscores. An existing workspace combines with the source on first sync. A new workspace appears after files sync from the server.</p>
        <div class="actions"><button class="btn primary" disabled={busy || !workspace || !status?.connected}>Save link</button><button type="button" class="btn" disabled={busy} onclick={() => folder = ''}>Cancel</button></div>
        <p class="muted">Saving keeps this link stopped until you choose Start sync.</p>
      </form>
    {/if}
    {#each status?.mappings ?? [] as mapping (mapping.id)}
      <article aria-label={`Folder link ${mapping.workspace}`}>
        <h4>{mapping.workspace}</h4><p class="path">{mapping.folder}</p>
        <p role="status">{mapping.busy ? 'Syncing…' : mapping.enabled ? mapping.error ? 'Paused — review the problem below.' : 'Running while Quantos is open.' : 'Stopped.'}
          {#if mapping.lastSynced} Last synced {new Date(mapping.lastSynced).toLocaleString()}.{/if}</p>
        {#if mapping.counts}<p class="muted">{mapping.counts.uploaded} sent · {mapping.counts.downloaded} received · {mapping.counts.conflicts} versions to review</p>{/if}
        {#if mapping.error}<p class="error" role="alert">{mapping.error} Original files and recovery data are kept.</p>{/if}
        <div class="actions">
          {#if mapping.enabled}<button class="btn" disabled={busy} onclick={() => perform(() => api.post(`folder-bridges/${mapping.id}/stop`))}>Stop sync</button>
          {:else}<button class="btn" disabled={busy || !status?.connected} onclick={() => start(mapping)}>Start sync</button>{/if}
          <button class="btn" disabled={busy} onclick={() => remove(mapping)}>Remove link</button>
        </div>
      </article>
    {/each}
  {/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}
</section>

<style>
  section { margin-top: 32px; }
  p { margin: 12px 0; line-height: 1.55; }
  .muted { color: var(--text-muted); font-size: 14px; }
  .path { overflow-wrap: anywhere; }
  .error { color: var(--danger); }
  form, article { margin: 16px 0; padding: 16px 0; border-bottom: 1px solid var(--line); }
  label { display: block; font-weight: 600; margin-bottom: 8px; }
  input { width: 100%; min-height: 44px; }
  .actions { display: flex; flex-wrap: wrap; gap: 10px; }
  .btn { min-height: 44px; }
</style>
