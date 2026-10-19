<script lang="ts">
  import { onMount } from 'svelte';
  import { updates } from '../lib/updates.svelte';
  import { isWindowsClient } from '../lib/platform';
  import { renderMarkdown } from '../lib/render';
  import { ago } from '../lib/today.svelte';

  /** Settings › Updates. */
  onMount(() => { updates.load(); });
  const s = $derived(updates.status);
  const mb = (n: number) => `${(n / 1048576).toFixed(0)} MB`;
</script>

<h2>Updates</h2>
{#if !s}
  <p class="faint">Loading…</p>
{:else}
  <p class="intro">This is Quantos <strong>{s.current}</strong>.
    {#if s.lastCheck}Last checked {ago(s.lastCheck)}.{/if}</p>

  <div class="row">
    <button class="btn" disabled={s.state === 'checking' || s.state === 'downloading'} onclick={() => updates.check()}>{s.state === 'checking' ? 'Checking…' : 'Check for updates'}</button>
    <label class="check"><input type="checkbox" checked={s.settings.autoCheck} onchange={(e) => updates.setAutoCheck((e.target as HTMLInputElement).checked)} /> Check once a day</label>
  </div>
  <p class="faint small">Checking asks GitHub (github.com/dave-patrick/Quantos) for the latest release. Nothing about you or your notes is sent, and Quantos doesn't contact anyone unless you check or turn on the daily check.</p>

  {#if s.error}<p class="warn">{s.error}</p>{/if}

  {#if s.latest}
    {#if s.available}
      <section class="release">
        <h3>{s.latest.name} is available</h3>
        <p class="faint small">Released {new Date(s.latest.published).toLocaleDateString()}{s.latest.size ? ` · ${mb(s.latest.size)}` : ''} · <a href={s.latest.page} target="_blank" rel="noopener noreferrer">Release page</a></p>
        {#if s.latest.notes}<div class="notes rendered">{@html renderMarkdown(s.latest.notes)}</div>{/if}

        {#if s.ready === s.latest.version}
          {#if isWindowsClient}
            <button class="btn primary" onclick={() => updates.install()}>Install and restart</button>
            <p class="faint small">Quantos saves your notes, closes, replaces its program files and starts again. Your vault and settings stay as they are.</p>
          {:else}
            <p class="small">Downloaded and checked. To install it, use the Windows client (Settings › Updates), or close Quantos and copy the files from <code>client-data\updates\{s.ready}\application</code> over your <code>application</code> folder.</p>
          {/if}
        {:else if s.state === 'downloading'}
          <div class="progress" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow={Math.round(s.progress * 100)}><span style={`width:${s.progress * 100}%`}></span></div>
          <p class="faint small">Downloading… {Math.round(s.progress * 100)}%</p>
        {:else}
          <button class="btn primary" onclick={() => updates.download()}>Download</button>
          <p class="faint small">The download is checked against the checksum GitHub publishes for it before anything is used.</p>
        {/if}
      </section>
    {:else}
      <p class="ok">You have the latest version.</p>
    {/if}
  {/if}
{/if}

<style>
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 8px; }
  h3 { font: 700 17px/1.3 var(--font-display); margin: 0 0 4px; }
  .intro { margin: 6px 0 12px; }
  .row { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
  .check { display: inline-flex; gap: 6px; align-items: center; font-size: 13.5px; color: var(--text-soft); }
  .small { font-size: 12.5px; max-width: 680px; }
  .warn { color: var(--warn); font-size: 13.5px; }
  .ok { color: var(--ok); }
  .release { margin-top: 18px; padding: 14px 16px; border: 1px solid var(--line-strong); border-radius: 10px; background: var(--bg-raised); max-width: 760px; }
  .notes { margin: 10px 0 14px; max-height: 320px; overflow: auto; font-size: 14px; }
  .progress { height: 8px; border-radius: 4px; background: var(--line); overflow: hidden; max-width: 360px; margin-top: 6px; }
  .progress span { display: block; height: 100%; background: var(--accent); transition: width .3s; }
</style>
