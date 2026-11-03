<script lang="ts">
  import { onMount } from 'svelte';
  import { api, ApiError } from '../lib/api';
  import { awaitApproval } from '../lib/approvals.svelte';
  import { app } from '../lib/state.svelte';
  import { layout } from '../lib/layout.svelte';
  import Icon from './Icon.svelte';

  /** Settings › MCP › HTTP endpoint: the app's own Streamable HTTP MCP, for clients that take a URL and a header. */
  interface Made { id: string; name: string; url: string; token: string; config: { url: string; headers: { Authorization: string } } }

  let url = $state('');
  let agent = $state('http-mcp');
  let made = $state<Made | null>(null);
  let busy = $state(false);
  let error = $state('');

  onMount(() => { api.get<{ url: string; defaultAgent: string }>('mcp-http').then((r) => { url = r.url; agent = r.defaultAgent; }).catch((e) => (error = (e as Error).message)); });

  async function create() {
    busy = true; error = ''; made = null;
    const body = { name: agent.trim() };
    try {
      try { made = await api.post<Made>('mcp-http/connection', body); }
      catch (e) {
        if (!(e instanceof ApiError) || !e.approval) throw e;
        const approval = await awaitApproval(e.approval);
        if (!approval) return;
        made = await api.post<Made>('mcp-http/connection', { ...body, approval });
      }
    } catch (e) { error = (e as Error).message; }
    finally { busy = false; }
  }

  async function copy(text: string, what: string) {
    try { await navigator.clipboard.writeText(text); app.toast(`${what} copied to the clipboard.`, 'ok'); }
    catch (e) { app.error(e); }
  }
</script>

<section aria-labelledby="http-mcp-title">
  <h3 id="http-mcp-title">HTTP endpoint (Streamable HTTP)</h3>
  <p class="faint">For an agent or tool that connects to a URL instead of starting a program. It is on this computer only, and nothing can use it until you create a key here.
    The key is an agent connection like any other: it starts with read access, shows under Paired connections, and
    <button class="link" onclick={() => layout.open({ kind: 'settings', title: 'Settings', arg: 'plugins' }, 'current')}>Settings › Plugins &amp; permissions</button> controls what it may do.</p>
  {#if url}<pre class="code">{url}</pre>{/if}
  <div class="field">
    <span>Agent name</span>
    <input class="input" aria-label="HTTP MCP agent name" bind:value={agent} />
    <span class="faint">how this connection is named in the permissions list</span>
  </div>
  <div class="row"><button class="btn primary" disabled={busy || !agent.trim()} onclick={create}>{busy ? 'Waiting…' : 'Create connection key'}</button></div>
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if made}
    <p class="faint">Copy this now: the key is shown once. Put it in the client's configuration (the header is <code>Authorization: Bearer …</code>).</p>
    <pre class="code">{JSON.stringify(made.config, null, 2)}</pre>
    <div class="row">
      <button class="btn" onclick={() => copy(JSON.stringify(made!.config, null, 2), 'Connection')}><Icon name="copy" size={14} /> Copy connection</button>
      <button class="btn" onclick={() => copy(made!.token, 'Key')}><Icon name="copy" size={14} /> Copy key only</button>
    </div>
  {/if}
  <p class="faint">Details and a curl test: <button class="link" onclick={() => layout.open({ kind: 'help', title: 'Help', arg: 'connecting' }, 'current')}>Help › Connect an AI remotely</button>.</p>
</section>

<style>
  h3 { font: 700 15px/1.3 var(--font-display); margin: 22px 0 6px; }
  .field { display: flex; align-items: center; gap: 8px; margin: 8px 0; flex-wrap: wrap; }
  .field > span:first-child { min-width: 96px; color: var(--text-soft); font-size: 13px; }
  .field .input { flex: 1; min-width: 220px; }
  .row { display: flex; gap: 8px; align-items: center; margin: 8px 0 12px; }
  .code { margin: 8px 0; padding: 10px 12px; border-radius: 8px; background: var(--bg-raised); border: 1px solid var(--line); font: 12px/1.5 var(--font-mono); color: var(--text-soft); overflow-x: auto; white-space: pre; }
  .error { color: var(--danger, #e5484d); font-size: 13px; }
  .link { border: 0; background: none; padding: 0; color: var(--accent); text-decoration: underline; cursor: pointer; font: inherit; }
</style>
