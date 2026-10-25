<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { bridge, isAndroidClient } from '../lib/platform';
  interface Info { connected: boolean; endpoint?: string; settings?: { enabled: boolean; agent: string; writeAccess: boolean } }
  let info = $state<Info | null>(null);
  let writeAccess = $state(false);
  let busy = $state(false);
  let error = $state('');
  async function load(preserveEdits = false) {
    const edited = preserveEdits && info?.settings && writeAccess !== info.settings.writeAccess;
    try { info = await api.get<Info>('sync/mcp'); if (!edited) writeAccess = info.settings?.writeAccess ?? false; error = ''; }
    catch (e) { error = (e as Error).message; }
  }
  onMount(() => {
    load();
    const timer = setInterval(() => { if (!busy) load(true); }, 5000);
    return () => clearInterval(timer);
  });
  async function copyConnection() {
    if (isAndroidClient) { bridge.send({ type: 'copy-mcp' }); return; }
    busy = true; error = '';
    try {
      const connection = await api.post('sync/mcp/connection', {});
      await navigator.clipboard.writeText(JSON.stringify({ mcpServers: { quantos: connection } }, null, 2));
      app.toast('MCP connection copied, including its agent key.', 'ok');
    } catch (e) { error = (e as Error).message; }
    finally { busy = false; }
  }
  async function save() {
    busy = true; error = '';
    try {
      info = await api.put<Info>('sync/mcp', { writeAccess });
      writeAccess = info.settings?.writeAccess ?? false;
      app.toast(writeAccess ? 'The server MCP can now edit notes and tasks.' : 'The server MCP is now read-only.', 'ok');
    } catch (e) { error = (e as Error).message; }
    finally { busy = false; }
  }
</script>

<section aria-labelledby="server-mcp-title">
  <h3 id="server-mcp-title">Shared server MCP</h3>
  <p>Connect agents directly to the server vault, even when your Windows and Android apps are closed.</p>
  {#if info?.connected}
    <label class="endpoint">MCP address<input class="input" readonly value={info.endpoint} /></label>
    <p class="faint">Use Streamable HTTP with the separate MCP key as a Bearer token. Keep the app’s server key private; it controls connections and permissions.</p>
    <button class="btn" disabled={busy || !info.settings?.enabled} onclick={copyConnection}>Copy MCP connection</button>
    <label class="access"><input type="checkbox" bind:checked={writeAccess} disabled={busy || !info.settings?.enabled} /> Allow the server MCP to create and edit notes and tasks</label>
    <p class="faint">{info.settings?.writeAccess ? 'Write access is enabled on the server.' : 'The server is read-only for MCP.'} MCP cannot change its own permissions, run shell commands or permanently delete files.</p>
    <button class="btn" disabled={busy || writeAccess === info.settings?.writeAccess || !info.settings?.enabled} onclick={save}>{busy ? 'Saving…' : 'Save server permissions'}</button>
    {#if !info.settings?.enabled}<p class="faint">The server needs its MCP key configured before agents can connect.</p>{/if}
  {:else if info}
    <p class="faint">Connect a server in Settings › Shared vault to configure its MCP access.</p>
  {:else if !error}<p role="status">Loading server settings…</p>{/if}
  {#if error}<p role="alert" class="error">{error}</p><button class="btn" onclick={() => load()}>Retry server connection</button>{/if}
</section>

<style>
  section { max-width: 760px; padding-bottom: 24px; margin-bottom: 24px; border-bottom: 1px solid var(--line); }
  h3 { font: 700 18px/1.3 var(--font-display); margin: 24px 0 8px; }
  p { line-height: 1.55; margin: 12px 0; }
  .endpoint { display: grid; gap: 8px; margin: 16px 0; }
  .endpoint input { min-height: 44px; }
  .access { display: flex; align-items: center; gap: 12px; min-height: 48px; margin: 16px 0; font-weight: 600; }
  .access input { width: 20px; height: 20px; flex-shrink: 0; }
  .error { color: var(--danger); }
</style>
