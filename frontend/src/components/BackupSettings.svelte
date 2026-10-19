<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { ago } from '../lib/today.svelte';
  import Icon from './Icon.svelte';

  /** Settings › Backups (Release 1.1): rolling backups with retention, checks and restore. */
  interface Settings { enabled: boolean; folder: string | null; intervalHours: number; keep: number; includeHistory: boolean }
  interface Info { name: string; path: string; created: string; size: number }
  interface Status { settings: Settings; folder: string; last: string | null; next: string | null; running: boolean; error: string | null; backups: Info[] }
  interface BackupNote { path: string; id: string | null; title: string; existsNow: boolean }

  let status = $state<Status | null>(null);
  let draft = $state<Settings | null>(null);
  let busy = $state('');
  let open = $state<string | null>(null);
  let notes = $state<BackupNote[]>([]);
  let noteFilter = $state('');

  async function load() {
    status = await api.get<Status>('backups');
    if (!draft) draft = { ...status.settings };
  }
  onMount(() => { load().catch((e) => app.error(e)); });

  const dirty = $derived(!!status && !!draft && JSON.stringify(draft) !== JSON.stringify(status.settings));
  const size = (b: number) => (b > 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`);
  const when = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });

  async function save() {
    busy = 'save';
    try { status = await api.put<Status>('backups/settings', { ...draft, folder: draft?.folder?.trim() || null }); draft = { ...status.settings }; app.toast('Backup settings saved.', 'ok'); }
    catch (e) { app.error(e); }
    finally { busy = ''; }
  }

  async function backUpNow() {
    busy = 'now';
    try { const b = await api.post<Info>('backups'); app.toast(`Backed up (${size(b.size)}).`, 'ok'); await load(); }
    catch (e) { app.error(e); }
    finally { busy = ''; }
  }

  async function check(b: Info) {
    busy = 'check-' + b.name;
    try {
      const r = await api.post<{ problems: string[] }>(`backups/${encodeURIComponent(b.name)}/verify`);
      if (r.problems.length) app.dialog = { title: 'This backup is damaged', message: r.problems.slice(0, 8).join('\n'), actions: [{ label: 'OK', kind: 'primary', run: () => (app.dialog = null) }] };
      else app.toast('Every file in this backup checks out.', 'ok');
    } catch (e) { app.error(e); }
    finally { busy = ''; }
  }

  async function showNotes(b: Info) {
    if (open === b.name) { open = null; return; }
    open = b.name; notes = []; noteFilter = '';
    try { notes = await api.get<BackupNote[]>(`backups/${encodeURIComponent(b.name)}/notes`); } catch (e) { app.error(e); open = null; }
  }

  async function restoreNote(b: Info, n: BackupNote) {
    const ok = await app.confirm(`Restore “${n.title}”?`,
      n.existsNow ? 'The note goes back to how it was in this backup. Its current text stays in the note’s history.' : 'The note is no longer in the vault; it comes back where it was.', 'Restore');
    if (!ok) return;
    try { await api.post(`backups/${encodeURIComponent(b.name)}/restore-note`, { path: n.path }); app.toast(`Restored “${n.title}”.`, 'ok'); notes = notes.map((x) => (x === n ? { ...x, existsNow: true } : x)); }
    catch (e) { app.error(e); }
  }

  async function restoreAll(b: Info) {
    const folder = await app.ask('Restore the whole backup', { message: 'It unpacks into a new folder; your current vault isn’t touched. Leave the folder empty to put it next to your vault.', placeholder: 'Folder (optional)', confirmLabel: 'Restore' });
    if (folder === null) return;
    busy = 'restore-' + b.name;
    try {
      const r = await api.post<{ folder: string }>(`backups/${encodeURIComponent(b.name)}/restore`, { folder: folder || null });
      const openIt = await app.confirm('Backup restored', `It's in ${r.folder}. Open it now? You can switch back from Settings › Vault.`, 'Open it');
      if (openIt) { await api.post('setup/open', { path: r.folder }); location.reload(); }
    } catch (e) { app.error(e); }
    finally { busy = ''; }
  }
</script>

<h2>Backups</h2>
<p class="faint intro">Quantos backs up the vault on a schedule while it's running: a zip of every note, task, attachment and setting, checked file by file after it's written. Keep backups on another drive, OneDrive or a network share for real safety. Restoring never overwrites your vault.</p>

{#if status && draft}
  <div class="state">
    <Icon name={status.error ? 'alert' : 'shield'} size={16} />
    {#if status.running}<span>Backing up…</span>
    {:else if status.last}<span>Last backup {ago(status.last)}{status.next && draft.enabled ? ` · next around ${when(status.next)}` : ''}</span>
    {:else}<span>No backups yet.</span>{/if}
    <button class="btn primary" disabled={busy === 'now' || status.running} onclick={backUpNow}>{busy === 'now' ? 'Backing up…' : 'Back up now'}</button>
  </div>
  {#if status.error}<p class="warn">{status.error}</p>{/if}

  <label class="check"><input type="checkbox" bind:checked={draft.enabled} /> Back up automatically</label>
  <div class="field"><span>Folder</span><input class="input" aria-label="Backup folder" bind:value={draft.folder} placeholder={status.folder} /></div>
  <div class="field"><span>How often</span>
    <select class="input narrow" aria-label="How often" bind:value={draft.intervalHours}>
      <option value={1}>Every hour</option><option value={6}>Every 6 hours</option><option value={12}>Twice a day</option><option value={24}>Every day</option><option value={168}>Every week</option>
    </select></div>
  <div class="field"><span>Keep</span><input class="input narrow" type="number" min="1" max="1000" aria-label="Backups to keep" bind:value={draft.keep} /> <span class="faint">newest backups (older ones are deleted)</span></div>
  <label class="check"><input type="checkbox" bind:checked={draft.includeHistory} /> Include version history (larger backups)</label>
  <div class="row"><button class="btn" disabled={!dirty || busy === 'save'} onclick={save}>Save settings</button></div>

  <h3>Backups of this vault</h3>
  {#if status.backups.length}
    <ul class="list">
      {#each status.backups as b (b.name)}
        <li>
          <div class="line">
            <span class="when">{when(b.created)}</span><span class="faint">{size(b.size)}</span>
            <span class="actions">
              <button class="btn ghost" disabled={busy === 'check-' + b.name} onclick={() => check(b)}>Check</button>
              <button class="btn ghost" onclick={() => showNotes(b)}>{open === b.name ? 'Hide notes' : 'Restore a note…'}</button>
              <button class="btn ghost" disabled={busy === 'restore-' + b.name} onclick={() => restoreAll(b)}>Restore all…</button>
            </span>
          </div>
          {#if open === b.name}
            <div class="notes">
              <input class="input" aria-label="Filter notes in this backup" placeholder="Filter by title" bind:value={noteFilter} />
              {#each notes.filter((n) => !noteFilter.trim() || n.title.toLowerCase().includes(noteFilter.trim().toLowerCase())).slice(0, 200) as n (n.path)}
                <div class="note"><span>{n.title}</span>{#if !n.existsNow}<span class="faint">deleted since</span>{/if}<button class="btn ghost" onclick={() => restoreNote(b, n)}>Restore</button></div>
              {:else}
                <p class="faint">{notes.length ? 'No match.' : 'Reading…'}</p>
              {/each}
            </div>
          {/if}
        </li>
      {/each}
    </ul>
  {:else}
    <p class="faint">None yet in {status.folder}.</p>
  {/if}
{/if}

<style>
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 8px; }
  h3 { font: 700 16px/1.3 var(--font-display); margin: 24px 0 8px; }
  .intro { max-width: 680px; font-size: 13.5px; }
  .state { display: flex; align-items: center; gap: 10px; margin: 14px 0; }
  .state .btn { margin-left: auto; }
  .field { display: flex; align-items: center; gap: 10px; margin: 8px 0; }
  .field > span:first-child { width: 110px; color: var(--text-soft); flex: none; }
  .field .input { max-width: 460px; }
  .field .narrow { width: auto; }
  .check { display: flex; gap: 8px; align-items: center; margin: 8px 0; }
  .row { margin-top: 10px; }
  .list { list-style: none; padding: 0; margin: 0; }
  .list li { border-bottom: 1px solid var(--line); padding: 6px 0; }
  .line { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
  .when { font-weight: 600; }
  .actions { margin-left: auto; display: flex; gap: 4px; }
  .notes { margin: 8px 0 4px 12px; display: grid; gap: 4px; max-height: 320px; overflow: auto; }
  .note { display: flex; align-items: center; gap: 10px; font-size: 13.5px; }
  .note span:first-child { flex: 1; }
</style>
