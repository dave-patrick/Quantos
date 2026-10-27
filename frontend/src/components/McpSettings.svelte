<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { layout } from '../lib/layout.svelte';
  import Icon from './Icon.svelte';
  import ServerMcpSettings from './ServerMcpSettings.svelte';
  import LocalConnections from './LocalConnections.svelte';
  import { isAndroidClient } from '../lib/platform';

  /** Settings › MCP: the Model Context Protocol server and how to point an agent at it. */
  interface McpInfo {
    program: string | null;
    args: string[];
    clientData: string;
    tools: { name: string; description: string }[];
    configs: Record<string, Record<string, unknown>>;
  }

  let info = $state<McpInfo | null>(null);
  let agent = $state('my-agent');
  let program = $state('');
  let saved = $state('');
  let busy = $state(false);
  let open = $state<string | null>('claudeDesktop');

  async function load() {
    info = await api.get<McpInfo>('mcp?agent=' + encodeURIComponent(agent));
  }
  onMount(() => { if (!isAndroidClient) load().catch((e) => app.error(e)); });

  const clients = [
    { key: 'claudeDesktop', name: 'Claude Desktop', where: 'claude_desktop_config.json' },
    { key: 'claudeCode', name: 'Claude Code', where: 'a command you run once' },
    { key: 'cursor', name: 'Cursor', where: '.cursor/mcp.json' },
    { key: 'codex', name: 'Codex', where: '.codex/config.toml' },
    { key: 'opencode', name: 'OpenCode', where: 'opencode.jsonc' }
  ];

  const json = (key: string) => JSON.stringify(info?.configs?.[key] ?? {}, null, 2);
  const ps = (text: string) => "'" + text.replaceAll("'", "''") + "'";
  const pairingCommand = () => info ? `& ${ps(info.program ?? 'quantos-cli')} status --agent ${ps(agent)} --client-data ${ps(info.clientData)}` : '';

  async function copy(text: string, what: string) {
    try {
      await navigator.clipboard.writeText(text);
      app.toast(`${what} copied to the clipboard.`, 'ok');
    } catch (e) { app.error(e); }
  }

  async function saveProgram() {
    busy = true;
    try {
      await api.put('mcp/settings', { program: program.trim() });
      saved = program.trim();
      await load();
      app.toast('Saved. The settings below now use this program.', 'ok');
    } catch (e) { app.error(e); }
    finally { busy = false; }
  }

  function useDetected() {
    if (!info?.program) return;
    program = info.program;
  }
</script>

<h2>MCP</h2>
<ServerMcpSettings />
{#if !isAndroidClient}
<h3>Local MCP</h3>
<p class="faint intro">
  MCP (Model Context Protocol) is how an AI agent reaches Quantos: it gets the same tools you use,
  to read and change your notes. Approve its first connection in the native prompt; it starts with
  read access, and you
  decide in
  <button class="link" onclick={() => layout.open({ kind: 'settings', title: 'Settings', arg: 'plugins' }, 'current')}>Settings › Plugins &amp; permissions</button>
  what else it may do.
</p>

<LocalConnections />

{#if info}
  <h3>Set up</h3>
  <p class="faint">
    Pair once in PowerShell before starting the agent. Match the code in the native Quantos prompt,
    then copy the configuration below into your agent. Quantos must be running.
  </p>

  <div class="field">
    <span>Agent name</span>
    <input class="input" aria-label="Agent name" bind:value={agent} onchange={() => load().catch((e) => app.error(e))} />
    <span class="faint">what this agent is called in the permissions list</span>
  </div>

  <div class="field">
    <span>Program</span>
    <input class="input" aria-label="Program path" bind:value={program} placeholder={info.program ?? 'quantos-cli.exe'} />
    <span class="faint">{info.program ? 'found' : 'not found next to the app'}</span>
  </div>
  <div class="row">
    <button class="btn primary" disabled={busy || program.trim() === saved} onclick={saveProgram}>Save program</button>
    {#if info.program && program.trim() !== info.program}<button class="btn" onclick={useDetected}>Use the one next to the app</button>{/if}
  </div>

  <pre class="code">{pairingCommand()}</pre>
  <div class="row"><button class="btn" onclick={() => copy(pairingCommand(), 'Pairing command')}><Icon name="copy" size={14} /> Copy pairing command</button></div>

  <h3>Configuration</h3>
  <div class="tabs" role="tablist" aria-label="Agent">
    {#each clients as c (c.key)}
      <button role="tab" aria-selected={open === c.key} class:on={open === c.key} onclick={() => (open = c.key)}>{c.name}</button>
    {/each}
  </div>
  {#each clients as c (c.key)}
    {#if open === c.key}
      <p class="faint">Put this in {c.where}:</p>
      <pre class="code">{json(c.key)}</pre>
      <div class="row"><button class="btn" onclick={() => copy(json(c.key), c.name + ' settings')}><Icon name="copy" size={14} /> Copy</button></div>
    {/if}
  {/each}

  <h3>What the agent can do</h3>
  <ul class="tools">
    {#each info.tools as t (t.name)}
      <li><code>{t.name}</code><span class="faint">{t.description}</span></li>
    {/each}
  </ul>
  <p class="faint">
    The full guide, with examples for Python frameworks and plain function calling, is in
    <button class="link" onclick={() => layout.open({ kind: 'help', title: 'Help', arg: 'agents' }, 'current')}>Help › AI agents</button>.
  </p>
{:else}
  <p class="faint">Loading…</p>
{/if}

{/if}

<style>
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 8px; }
  h3 { font: 700 15px/1.3 var(--font-display); margin: 22px 0 6px; }
  .intro { max-width: 60ch; margin-bottom: 14px; }
  .field { display: flex; align-items: center; gap: 8px; margin: 8px 0; flex-wrap: wrap; }
  .field > span:first-child { min-width: 96px; color: var(--text-soft); font-size: 13px; }
  .field .input { flex: 1; min-width: 220px; }
  .row { display: flex; gap: 8px; align-items: center; margin: 8px 0 12px; }
  .tabs { display: flex; gap: 4px; flex-wrap: wrap; margin: 6px 0 10px; }
  .tabs button { padding: 5px 10px; border: 1px solid var(--line); border-radius: 7px; background: transparent; color: var(--text-soft); font-size: 13px; }
  .tabs button.on { border-color: var(--accent); color: var(--accent); }
  .code { margin: 0; padding: 10px 12px; border-radius: 8px; background: var(--bg-raised); border: 1px solid var(--line); font: 12px/1.5 var(--font-mono); color: var(--text-soft); overflow-x: auto; white-space: pre; }
  .tools { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
  .tools li { display: grid; grid-template-columns: 190px 1fr; gap: 10px; align-items: baseline; }
  .tools code { font: 12px var(--font-mono); color: var(--accent); }
  .tools span { font-size: 13px; }
  .link { border: 0; background: none; padding: 0; color: var(--accent); text-decoration: underline; cursor: pointer; font: inherit; }
</style>
