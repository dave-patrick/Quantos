<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { openNode } from '../../lib/links.svelte';
  import type { Diagnostic, ReconcileReport, Status } from '../../lib/types';
  import Icon from '../Icon.svelte';

  let status = $state<Status | null>(null);
  let integrity = $state<{ diagnostics: Diagnostic[]; checked: number } | null>(null);
  let busy = $state(false);

  async function load() { status = await app.refreshStatus(); }

  async function rebuild() {
    busy = true;
    try {
      const report = await api.post<ReconcileReport>('system/rebuild');
      app.toast(`Rebuilt the cache from ${report.scanned} files in ${report.elapsed.slice(0, 8)}.`, 'ok');
      await load();
    } catch (e) { app.error(e); } finally { busy = false; }
  }

  async function check() {
    busy = true;
    try { integrity = await api.get('diagnostics/integrity'); }
    catch (e) { app.error(e); } finally { busy = false; }
  }

  onMount(load);
  const report = $derived(status?.reconcile);
  const all = $derived([...(report?.diagnostics ?? []), ...(integrity?.diagnostics ?? [])]);
</script>

<div class="scroll view">
  <div class="inner">
    <h1>Vault diagnostics</h1>
    {#if status}
      <dl class="facts">
        <dt>Vault</dt><dd class="mono">{status.vault}</dd>
        <dt>Client data</dt><dd class="mono">{status.clientData}</dd>
        <dt>Nodes indexed</dt><dd>{status.nodeCount}</dd>
        <dt>Backend</dt><dd>Version {status.version}, process {status.pid}, port {status.port}</dd>
        <dt>Time zone</dt><dd>{status.timeZone} (UTC{status.utcOffset.startsWith('-') ? '' : '+'}{status.utcOffset.slice(0, -3)})</dd>
        <dt>Connected as</dt><dd>{status.actor.windowsClient ? 'Windows client' : status.actor.native ? 'Command line' : 'Browser session'}</dd>
      </dl>
      {#if status.syncedFolderWarning}<div class="notice warn"><Icon name="alert" size={16} /> {status.syncedFolderWarning}</div>{/if}

      <section>
        <h2>Last startup check</h2>
        {#if report}
          {#if report.rebuilt}
            <p class="muted">Indexed {report.scanned} files from scratch (the cache was new or rebuilt){report.adopted ? `, ${report.adopted} given a new identity` : ''}{report.tempFilesRemoved ? `, ${report.tempFilesRemoved} leftover temporary files cleaned up` : ''}.</p>
          {:else}
            <p class="muted">Scanned {report.scanned} files: {report.changed} changed and {report.added} added outside Quantos, {report.removed} removed, {report.adopted} given a new identity, {report.tempFilesRemoved} leftover temporary files cleaned up.</p>
          {/if}
          {#each report.recovered as r (r.opId)}
            <div class="notice {r.outcome === 'completed' ? 'ok' : 'warn'}"><Icon name="refresh" size={16} /> Interrupted “{r.description}” was {r.outcome}. {r.problems.join(' ')}</div>
          {/each}
        {/if}
        <div class="row">
          <button class="btn" disabled={busy} onclick={rebuild}><Icon name="refresh" size={14} /> Rebuild cache</button>
          <button class="btn" disabled={busy} onclick={check}><Icon name="shield" size={14} /> Check integrity</button>
        </div>
        <p class="faint small">The cache lives outside the vault and can always be rebuilt from your files.</p>
      </section>

      <section>
        <h2>Linked folders</h2>
        {#if status.linked?.length}
          <p class="muted">Folders linked in from elsewhere on this computer. Their notes are read and written where they are.</p>
          <ul class="linked">
            {#each status.linked as f (f.name)}
              <li>
                <span class="name">{f.name}</span>
                {#if f.exists}<span class="ok">there</span>{:else}<span class="warn">not there any more</span>{/if}
                <span class="mono faint">{f.path}</span>
              </li>
            {/each}
          </ul>
        {:else}
          <p class="muted">No folders are linked in. *Link a folder…* in the workspace menu adds one.</p>
        {/if}
      </section>

      <section>
        <h2>Problems</h2>
        {#if all.length === 0}<p class="muted">No problems found.</p>{/if}
        {#each all as d, i (i)}
          <div class="diag {d.severity}">
            <Icon name={d.severity === 'error' ? 'alert' : d.severity === 'warning' ? 'alert' : 'info'} size={15} />
            <div><div>{d.message}</div>{#if d.path}<div class="mono faint">{d.path}</div>{/if}</div>
            {#if d.id}<button class="btn ghost" onclick={() => openNode(d.id!, 'new')}>Open</button>{/if}
          </div>
        {/each}
      </section>
    {/if}
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .inner { max-width: 900px; margin: 0 auto; padding: 24px 0 80px; }
  h1 { font: 700 28px/1.2 var(--font-display); margin-bottom: 18px; }
  h2 { font: 700 17px/1.3 var(--font-display); margin: 26px 0 8px; }
  .facts { display: grid; grid-template-columns: 160px 1fr; gap: 6px 16px; margin: 0; font-size: 14px; }
  dt { color: var(--text-faint); }
  dd { margin: 0; overflow-wrap: anywhere; }
  .mono { font-family: var(--font-mono); font-size: 12.5px; }
  .notice { display: flex; gap: 8px; padding: 10px 12px; border-radius: 8px; margin: 10px 0; font-size: 14px; }
  .notice.warn { background: var(--warn-wash); color: var(--warn); }
  .notice.ok { background: var(--ok-wash); color: var(--ok); }
  .row { display: flex; gap: 8px; margin-top: 12px; }
  .small { font-size: 12.5px; }
  .diag { display: flex; gap: 10px; align-items: flex-start; padding: 10px 0; border-bottom: 1px solid var(--line); font-size: 14px; }
  .diag > div { flex: 1; }
  .diag.warning :global(.icon) { color: var(--warn); }
  .diag.error :global(.icon) { color: var(--danger); }
  .diag.info :global(.icon) { color: var(--accent); }
  .linked { list-style: none; margin: 0; padding: 0; }
  .linked li { display: flex; gap: 12px; align-items: baseline; padding: 6px 0; border-bottom: 1px solid var(--line); font-size: 14px; }
  .linked .name { font-weight: 600; min-width: 120px; }
  .linked .ok { color: var(--ok); }
  .linked .warn { color: var(--warn); }
</style>
