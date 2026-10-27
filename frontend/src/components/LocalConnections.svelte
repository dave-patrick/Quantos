<script lang="ts">
  import { onMount } from 'svelte';
  import { api, ApiError } from '../lib/api';
  import { awaitApproval } from '../lib/approvals.svelte';
  import { app } from '../lib/state.svelte';

  interface Connection { id: string; kind: 'Agent' | 'Cli'; name: string; created: string }
  let connections = $state<Connection[] | null>(null);
  let error = $state('');
  let busy = $state<string | null>(null);
  let loading = $state(false);

  async function load() {
    loading = true;
    try { connections = await api.get<Connection[]>('auth/credentials'); error = ''; }
    catch (e) { error = (e as Error).message; }
    finally { loading = false; }
  }
  onMount(() => { void load(); });

  async function revoke(connection: Connection) {
    busy = connection.id; error = '';
    const path = `auth/credentials/${encodeURIComponent(connection.id)}/revoke`;
    try {
      try { await api.post(path); }
      catch (e) {
        if (!(e instanceof ApiError) || !e.approval) throw e;
        const approval = await awaitApproval(e.approval);
        if (!approval) return;
        await api.post(path, { approval });
      }
      await load();
      app.toast(`Connection for ${connection.name} revoked.`, 'ok');
    } catch (e) { error = (e as Error).message; }
    finally { busy = null; }
  }
</script>

<section aria-labelledby="local-connections-title">
  <div class="heading">
    <h3 id="local-connections-title">Paired connections</h3>
    <button class="btn" disabled={loading || busy !== null} onclick={load}>{loading ? 'Refreshing…' : 'Refresh connections'}</button>
  </div>
  <p class="faint">When a local CLI or agent first connects, match its pairing code in the native Quantos prompt.
    Agents keep their own permissions. A personal CLI connection can edit notes and open signed-in browser windows.</p>
  {#if connections?.length}
    <ul>
      {#each connections as connection (connection.id)}
        <li>
          <div class="identity">
            <strong>{connection.name}</strong>
            <span class="faint">{connection.kind === 'Cli' ? 'Personal CLI' : 'Agent'} · Paired {new Date(connection.created).toLocaleDateString()} · Connection {connection.id.slice(-8)}</span>
          </div>
          <button class="btn" aria-label={`Revoke ${connection.name} connection ${connection.id.slice(-8)}`} disabled={busy !== null || loading} onclick={() => revoke(connection)}>{busy === connection.id ? 'Waiting for confirmation…' : 'Revoke'}</button>
        </li>
      {/each}
    </ul>
    <p class="faint">Revoking disconnects that credential. Pairing again requires a new native confirmation.</p>
  {:else if connections}
    <p class="faint">No local connections are paired yet. Start your CLI or agent to request one.</p>
  {:else if !error}<p role="status">Loading paired connections…</p>{/if}
  {#if error}<p role="alert" class="error">{error}</p>{/if}
</section>

<style>
  section { margin: 24px 0; }
  .heading { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px; }
  h3 { font: 700 15px/1.3 var(--font-display); margin: 0; }
  p { line-height: 1.55; max-width: 70ch; margin: 12px 0; }
  ul { list-style: none; margin: 0; padding: 0; }
  li { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px; padding: 12px 0; border-bottom: 1px solid var(--line); }
  .identity { display: grid; gap: 4px; min-width: 0; overflow-wrap: anywhere; }
  .identity span { font-size: 13px; }
  .btn { min-height: 44px; }
  .error { color: var(--danger); }
</style>
