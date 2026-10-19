<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../../lib/state.svelte';
  import { layout } from '../../lib/layout.svelte';
  import { updates } from '../../lib/updates.svelte';
  import { isWindowsClient } from '../../lib/platform';
  import wordmark from '../../assets/quantos-wordmark.svg?raw';
  import license from '../../../../LICENSE?raw';

  /** About: the version, where things are kept, the licence and the open-source work Quantos is built on. */
  onMount(() => { updates.load(); });

  const COMPONENTS: [string, string, string][] = [
    ['Svelte', 'MIT', 'https://svelte.dev'],
    ['CodeMirror and Lezer', 'MIT', 'https://codemirror.net'],
    ['marked', 'MIT', 'https://marked.js.org'],
    ['DOMPurify', 'MPL-2.0 or Apache-2.0', 'https://github.com/cure53/DOMPurify'],
    ['KaTeX', 'MIT', 'https://katex.org'],
    ['Mermaid', 'MIT', 'https://mermaid.js.org'],
    ['node-diff3', 'MIT', 'https://github.com/bhousel/node-diff3'],
    ['world-atlas (Natural Earth data, public domain)', 'ISC', 'https://github.com/topojson/world-atlas'],
    ['.NET and ASP.NET Core', 'MIT', 'https://dot.net'],
    ['Microsoft.Data.Sqlite and SQLite', 'MIT; SQLite is public domain', 'https://sqlite.org'],
    ['Markdig', 'BSD-2-Clause', 'https://github.com/xoofx/markdig'],
    ['YamlDotNet', 'MIT', 'https://github.com/aaubry/YamlDotNet'],
    ['PowerShell', 'MIT', 'https://github.com/PowerShell/PowerShell'],
    ['Microsoft Edge WebView2 SDK', 'BSD-style (Microsoft)', 'https://aka.ms/webview2'],
    ['Anthropic C# SDK', 'MIT', 'https://github.com/anthropics/anthropic-sdk-csharp'],
    ['Source Sans 3, IBM Plex Mono, Chakra Petch fonts', 'SIL Open Font License 1.1', 'https://openfontlicense.org']
  ];

  const s = $derived(app.status);
</script>

<div class="scroll about">
  <div class="inner">
    <div class="mark" role="img" aria-label="Quantos">{@html wordmark}</div>
    <p class="tag">Notes, tasks and projects in plain Markdown files you own.</p>

    <dl class="facts">
      <dt>Version</dt><dd>{updates.status?.current ?? s?.version ?? '…'}
        {#if updates.status?.available}<button class="link" onclick={() => layout.open({ kind: 'settings', title: 'Settings', arg: 'updates' }, 'current')}>Version {updates.status.latest?.version} is available</button>
        {:else}<button class="link" onclick={() => layout.open({ kind: 'settings', title: 'Settings', arg: 'updates' }, 'current')}>Check for updates</button>{/if}</dd>
      <dt>Running in</dt><dd>{isWindowsClient ? 'The Windows client' : 'A browser'}{s?.safeMode ? ' · safe mode' : ''}</dd>
      <dt>Vault</dt><dd class="path">{s?.vault ?? 'None open'}</dd>
      <dt>App data</dt><dd class="path">{s?.clientData ?? ''}</dd>
      <dt>Notes</dt><dd>{s?.nodeCount ?? '…'} in {s?.workspaces?.length ?? 0} {s?.workspaces?.length === 1 ? 'workspace' : 'workspaces'}</dd>
      <dt>Time zone</dt><dd>{s?.timeZone} (UTC{s?.utcOffset?.replace(/:00$/, "")})</dd>
    </dl>

    <div class="row">
      <button class="btn" onclick={() => layout.open({ kind: 'help', title: 'Help' }, 'current')}>Open Help</button>
      <a class="btn ghost" href="https://github.com/dave-patrick/Quantos" target="_blank" rel="noopener noreferrer">Quantos on GitHub</a>
      <a class="btn ghost" href="https://github.com/dave-patrick/Quantos/releases" target="_blank" rel="noopener noreferrer">Release notes</a>
    </div>

    <h2>Built with</h2>
    <p class="faint small">Quantos includes these open-source projects. Thank you to everyone who makes them.</p>
    <table class="parts">
      <thead><tr><th>Project</th><th>Licence</th></tr></thead>
      <tbody>{#each COMPONENTS as [name, lic, url] (name)}<tr><td><a href={url} target="_blank" rel="noopener noreferrer">{name}</a></td><td>{lic}</td></tr>{/each}</tbody>
    </table>

    <h2>Licence</h2>
    <pre class="licence">{license}</pre>
  </div>
</div>

<style>
  .about { flex: 1; }
  .inner { max-width: 760px; margin: 0 auto; padding: 34px 28px 50px; }
  .mark { height: 46px; color: var(--text); }
  .mark :global(svg) { height: 100%; width: auto; }
  .tag { color: var(--text-soft); margin: 12px 0 20px; font-size: 15.5px; }
  .facts { display: grid; grid-template-columns: 120px 1fr; gap: 8px 16px; margin: 0 0 18px; font-size: 14px; }
  .facts dt { color: var(--text-muted); }
  .facts dd { margin: 0; display: flex; flex-wrap: wrap; gap: 6px 14px; align-items: baseline; }
  .path { font: 12.5px var(--font-mono); word-break: break-all; }
  .link { border: 0; background: none; padding: 0; color: var(--accent); font: inherit; cursor: pointer; }
  .link:hover { text-decoration: underline; }
  .row { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 10px; }
  .row a { text-decoration: none; }
  h2 { font: 700 18px/1.3 var(--font-display); margin: 28px 0 8px; }
  .small { font-size: 13px; }
  .parts { width: 100%; border-collapse: collapse; font-size: 13.5px; }
  .parts th { text-align: left; color: var(--text-muted); font-weight: 600; padding: 5px 0; border-bottom: 1px solid var(--line); }
  .parts td { padding: 5px 12px 5px 0; border-bottom: 1px solid var(--line); }
  .parts a { color: var(--text); }
  .parts a:hover { color: var(--accent); }
  .licence { font: 12px/1.55 var(--font-mono); color: var(--text-soft); white-space: pre-wrap; background: var(--bg-raised); border: 1px solid var(--line); border-radius: 8px; padding: 12px 14px; }
</style>
