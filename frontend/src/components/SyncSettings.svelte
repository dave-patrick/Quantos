<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { bridge, isAndroidClient } from '../lib/platform';
  interface Conflict { id: string; path: string; localHash: string | null; remoteHash: string | null; detected: string }
  interface Status { url: string | null; busy: boolean; lastSynced: string | null; error: string | null; conflicts: Conflict[] }
  let status = $state<Status | null>(null);
  let url = $state('');
  let token = $state('');
  let busy = $state(false);
  let failure = $state('');
  async function refresh() { status = await api.get<Status>('sync/'); }
  onMount(() => {
    refresh().then(() => { url = status?.url ?? ''; }).catch((e) => { failure = e.message; });
    const timer = setInterval(() => refresh().catch(() => {}), 5000);
    return () => clearInterval(timer);
  });
  async function perform(action: () => Promise<unknown>) {
    busy = true; failure = '';
    try { await action(); await refresh(); }
    catch (e) { failure = (e as Error).message; }
    finally { busy = false; }
  }
  async function connect() {
    const ok = await app.confirm('Connect this vault?', 'This vault and the shared vault will be combined. Changes sync in both directions, including moving notes to Trash. Different versions are kept for review. Keep a backup before connecting an existing vault.', 'Connect');
    if (!ok) return;
    await perform(async () => {
      await api.put('sync/', { url, token: token || null }); token = '';
      await api.post('sync/run', {});
    });
  }
  async function resolve(conflict: Conflict, keepLocal: boolean) {
    const ok = await app.confirm(keepLocal ? 'Use this device’s version?' : 'Use the server’s version?', `${conflict.path}\nBoth reviewed versions remain in recovery storage after your choice.`, 'Use this version');
    if (ok) await perform(() => api.post(`sync/conflicts/${conflict.id}`, { keepLocal }));
  }
</script>

<section aria-labelledby="sync-heading">
  <h2 id="sync-heading">Shared vault</h2>
  <p>Keep the same notes, tasks and workspaces on Windows and Android. Your vault stays available offline; changes sync automatically when the server is reachable.</p>
  <p class="muted">A server is optional. Leave this disconnected to use Quantos only on this device.</p>
  <form onsubmit={(e) => { e.preventDefault(); connect(); }}>
    <label>Server address<input class="input" type="url" required bind:value={url} placeholder="https://your-server.example" autocapitalize="none" spellcheck="false" /></label>
    <label>Server key<input class="input" type="password" bind:value={token} autocomplete="off" placeholder={status?.url ? 'Leave blank to keep the saved key' : 'Enter your server key'} /></label>
    <p class="muted">The key is encrypted for this device and kept outside the vault. Moving the server later only requires changing its address here and transferring its persistent data.</p>
    <div class="actions">
      <button class="btn primary" disabled={busy || !url}>{status?.url ? 'Save connection' : 'Connect'}</button>
      {#if status?.url}
        <button class="btn" type="button" disabled={busy || status.busy} onclick={() => perform(() => api.post('sync/run', {}))}>Sync now</button>
        <button class="btn" type="button" disabled={busy} onclick={async () => {
          if (await app.confirm('Disconnect this device?', 'Your local vault and any unsynced changes stay on this device. The server vault is kept.', 'Disconnect'))
            await perform(() => api.del('sync/'));
        }}>Disconnect</button>
      {/if}
    </div>
  </form>
  <p role="status">{busy || status?.busy ? 'Syncing…' : status?.url ? status.lastSynced ? `Last synced ${new Date(status.lastSynced).toLocaleString()}` : 'Waiting for the first sync.' : 'This device is using a local vault.'}</p>
  {#if failure || status?.error}<p class="error" role="alert">{failure || status?.error} Your local changes are kept. Retry when the server is available.</p>{/if}
  {#if status?.conflicts.length}
    <h3>Versions to review</h3>
    <p>Both devices changed these files. Neither version has been overwritten.</p>
    <ul class="conflicts">
      {#each status.conflicts as conflict (conflict.id)}
        <li>
          <p class="path">{conflict.path}</p>
          <p class="muted">Device: {conflict.localHash ? 'edited' : 'deleted'} · Server: {conflict.remoteHash ? 'edited' : 'deleted'}</p>
          <div class="actions">
            {#if conflict.localHash}<a class="btn" href={`/api/sync/conflicts/${conflict.id}/version?local=true`} download>Save device copy</a>{/if}
            {#if conflict.remoteHash}<a class="btn" href={`/api/sync/conflicts/${conflict.id}/version?local=false`} download>Save server copy</a>{/if}
            <button class="btn" disabled={busy} onclick={() => resolve(conflict, true)}>Use device version</button>
            <button class="btn" disabled={busy} onclick={() => resolve(conflict, false)}>Use server version</button>
          </div>
        </li>
      {/each}
    </ul>
  {/if}
  {#if isAndroidClient}
    <h3>Android files</h3>
    <p>Quantos keeps its vault in private device storage. Export a copy before uninstalling the app.</p>
    <div class="actions">
      <button class="btn" onclick={() => bridge.send({ type: 'export-vault' })}>Export vault</button>
      <button class="btn" onclick={() => bridge.send({ type: 'import-vault' })}>Import vault copy</button>
      <button class="btn" onclick={() => bridge.send({ type: 'print' })}>Print this screen</button>
    </div>
  {/if}
</section>

<style>
  section { max-width: 760px; }
  p { margin: 12px 0; line-height: 1.55; }
  form { margin: 24px 0; }
  label { display: grid; gap: 8px; margin: 16px 0; font-weight: 600; }
  input { width: 100%; min-height: 44px; }
  .actions { display: flex; flex-wrap: wrap; gap: 10px; }
  .actions .btn { min-height: 44px; }
  .muted { color: var(--text-muted); font-size: 14px; }
  .error { color: var(--danger); }
  h3 { margin-top: 32px; }
  .conflicts { list-style: none; padding: 0; }
  li { padding: 16px 0; border-bottom: 1px solid var(--line); }
  .path { overflow-wrap: anywhere; font-weight: 600; }
</style>
