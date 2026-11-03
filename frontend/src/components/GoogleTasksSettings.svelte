<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { bridge, isNativeClient } from '../lib/platform';
  import { NEW_LIST, directionLabel, intervalChoices, statusLabel, syncLine, validateMappings, type GooglePreview, type GoogleStatus, type MappingRow } from '../lib/googleTasks';

  let status = $state<GoogleStatus | null>(null);
  let clientId = $state('');
  let clientSecret = $state('');
  let busy = $state(false);
  let failure = $state('');
  let authUrl = $state('');
  let lists = $state<{ id: string; title: string }[]>([]);
  /** An editable row: `k` keeps its identity while rows are added or removed; `newName` is the name for a list about to be created. */
  type Row = MappingRow & { k: number; newName: string };
  let nextKey = 0;
  const makeRow = (r: MappingRow): Row => ({ ...r, k: nextKey++, newName: '' });
  let rows = $state<Row[]>([]);
  let previews = $state<Record<string, GooglePreview>>({});
  let waitingSince = 0;
  let pendingHeading: HTMLElement | undefined = $state();

  const connected = $derived(status?.state === 'Connected');
  const mappingsError = $derived(validateMappings(rows.filter((r) => r.workspace || r.listId)));

  async function refresh() {
    status = await api.get<GoogleStatus>('google-tasks/');
    if (status.flow === 'idle') waitingSince = 0;
  }
  async function loadLists() {
    if (status?.state !== 'Connected') { lists = []; return; }
    try { lists = await api.get<{ id: string; title: string }[]>('google-tasks/lists'); } catch (e) { failure = (e as Error).message; }
  }
  function syncRows() { rows = (status?.mappings ?? []).map((m) => makeRow({ workspace: m.workspace, listId: m.listId, listTitle: m.listTitle })); }

  onMount(() => {
    refresh().then(() => { syncRows(); return loadLists(); }).catch((e) => { failure = e.message; });
    const timer = setInterval(() => {
      if (waitingSince && Date.now() - waitingSince > 10 * 60_000) { waitingSince = 0; failure = 'Timed out waiting for Google. Press Connect to try again.'; }
      const was = status?.state;
      refresh().then(() => { if (was !== 'Connected' && status?.state === 'Connected') { syncRows(); authUrl = ''; return loadLists(); } }).catch(() => {});
    }, 3000);
    return () => clearInterval(timer);
  });

  async function perform(action: () => Promise<unknown>) {
    busy = true; failure = '';
    try { await action(); await refresh(); }
    catch (e) { failure = (e as Error).message; }
    finally { busy = false; }
  }

  async function saveCredentials() {
    await perform(async () => {
      await api.put('google-tasks/settings', { clientId: clientId || undefined, clientSecret: clientSecret || undefined });
      clientSecret = '';
    });
  }
  async function connect() {
    await perform(async () => {
      const { authUrl: url } = await api.post<{ authUrl: string }>('google-tasks/connect', {});
      waitingSince = Date.now();
      if (isNativeClient) bridge.send({ type: 'open-external', url });
      else authUrl = url;
    });
  }
  async function saveMappings() {
    await perform(async () => {
      // A list is created once: its id goes back into the row at once, so pressing Save again after a failure reuses it.
      for (const row of rows) {
        if (row.listId !== NEW_LIST) continue;
        const created = await api.post<{ id: string; title: string }>('google-tasks/lists', { title: row.newName.trim() || row.workspace });
        row.listId = created.id; row.listTitle = created.title;
      }
      await api.put('google-tasks/settings', { mappings: rows.filter((r) => r.workspace && r.listId).map(({ workspace, listId, listTitle }) => ({ workspace, listId, listTitle })) });
      previews = {}; // counts shown for an earlier list would be wrong for a changed one, and the server forgets them too
      await refresh(); syncRows(); await loadLists();
    });
  }
  async function preview(workspace: string) {
    await perform(async () => { previews[workspace] = await api.post<GooglePreview>('google-tasks/preview', { workspace }); });
  }
  async function confirm(workspace: string) {
    const p = previews[workspace];
    if (!p) return;
    await perform(async () => {
      await api.post('google-tasks/confirm', { workspace, cutoff: p.cutoff });
      delete previews[workspace];
      await api.post('google-tasks/sync', {});
    });
  }
  async function resolveDeletes(action: 'apply' | 'cancel') {
    const ok = await app.confirm(action === 'apply' ? 'Apply these deletions?' : 'Cancel these deletions?',
      action === 'apply' ? 'The tasks listed will be deleted in Google, or moved to Trash here, as shown.' : 'Nothing is deleted. Tasks you removed here come back from Google as new tasks, and tasks removed in Google stay here.',
      action === 'apply' ? 'Apply' : 'Cancel deletions');
    if (ok) await perform(() => api.post('google-tasks/deletes', { action }));
  }
  async function disconnect() {
    if (await app.confirm('Disconnect Google Tasks?', 'Quantos stops syncing and signs out of Google. Your tasks stay here and in Google.', 'Disconnect'))
      await perform(async () => { await api.del('google-tasks/'); rows = []; lists = []; previews = {}; authUrl = ''; });
  }
  // Focus the heading when deletions first appear, not on every poll that replaces `status`.
  const hasPending = $derived(!!status?.pendingDeletes.length);
  $effect(() => { if (hasPending) tick().then(() => pendingHeading?.focus()); });
  const freeWorkspaces = (index: number) => app.workspaces.filter((w) => w === rows[index].workspace || !rows.some((r, i) => i !== index && r.workspace === w));
</script>

<section aria-labelledby="gt-heading">
  <h2 id="gt-heading">Google Tasks</h2>
  <p>Keep chosen Quantos workspaces in step with Google Tasks lists, so your tasks show up in Google’s apps. Titles, notes, due dates, done and subtasks sync both ways. Everything else about a task stays in Quantos. This is optional and off until you connect.</p>
  <p role="status" class="status">{statusLabel(status)}</p>
  {#if failure || status?.error}<p class="error" role="alert">{failure || status?.error}</p>{/if}
  {#if status?.notice}<p class="muted" role="status">{status.notice}</p>{/if}

  {#if !status?.builtInClient}
  <details open={status?.state === 'Off' || status?.state === 'NeedsReconnect'}>
    <summary>Set up your Google client</summary>
    <ol>
      <li>In the <strong>Google Cloud Console</strong>, create a project and turn on the <strong>Google Tasks API</strong>.</li>
      <li>Create an OAuth client of type <strong>Desktop app</strong>.</li>
      <li>On the consent screen, set the publishing status to <strong>In production</strong>. Google signs you out after 7 days while it is “Testing”. Google will warn that the app is unverified; that is expected for your own client.</li>
      <li>Paste the client ID and secret below. They stay on this device, outside your vault.</li>
    </ol>
    <form onsubmit={(e) => { e.preventDefault(); saveCredentials(); }}>
      <label>Client ID<input class="input" bind:value={clientId} autocomplete="off" autocapitalize="none" spellcheck="false" placeholder={status?.hasCredentials ? 'Saved — type to replace' : ''} /></label>
      <label>Client secret<input class="input" type="password" bind:value={clientSecret} autocomplete="off" placeholder={status?.hasCredentials ? 'Saved — leave blank to keep' : ''} /></label>
      <div class="actions"><button class="btn" disabled={busy || (!clientId && !clientSecret)}>Save</button></div>
    </form>
  </details>
  {/if}

  <div class="actions">
    {#if status?.state === 'Off' || status?.state === 'NeedsReconnect'}
      <button class="btn primary" disabled={busy || !status?.hasCredentials} onclick={connect}>{status?.state === 'NeedsReconnect' ? 'Reconnect' : 'Connect'}</button>
    {/if}
    {#if status?.state && status.state !== 'Off'}
      <button class="btn" disabled={busy} onclick={disconnect}>Disconnect</button>
    {/if}
  </div>
  {#if authUrl}<p><a class="btn" href={authUrl} target="_blank" rel="noopener noreferrer">Continue to Google</a> <span class="muted">Opens Google in a new tab. Come back here when it says you can close it.</span></p>{/if}
  {#if status?.flow === 'waiting' && !authUrl}<p class="muted" role="status">Waiting for you to finish signing in with Google…</p>{/if}

  {#if connected}
    <h3>Lists</h3>
    <p class="muted">Each workspace can sync with one Google list, and each list with one workspace.</p>
    <ul class="rows">
      {#each rows as row, i (row.k)}
        <li>
          <label>Workspace
            <select class="input" bind:value={row.workspace}>
              <option value="">Choose…</option>
              {#each freeWorkspaces(i) as w (w)}<option value={w}>{w}</option>{/each}
            </select>
          </label>
          <label>Google list
            <select class="input" bind:value={row.listId} onchange={() => { row.listTitle = lists.find((l) => l.id === row.listId)?.title ?? ''; }}>
              <option value="">Choose…</option>
              {#each lists as l (l.id)}<option value={l.id}>{l.title}</option>{/each}
              <option value={NEW_LIST}>New list…</option>
            </select>
          </label>
          {#if row.listId === NEW_LIST}<label>New list name<input class="input" bind:value={row.newName} placeholder={row.workspace || 'Quantos'} /></label>{/if}
          <button class="btn" type="button" onclick={() => { rows = rows.filter((_, j) => j !== i); }}>Remove</button>
        </li>
      {/each}
    </ul>
    {#if mappingsError}<p class="error" role="alert">{mappingsError}</p>{/if}
    <div class="actions">
      <button class="btn" type="button" onclick={() => { rows = [...rows, makeRow({ workspace: '', listId: '', listTitle: '' })]; }}>Add a list</button>
      <button class="btn primary" type="button" disabled={busy || !!mappingsError} onclick={saveMappings}>Save lists</button>
    </div>

    {#each status?.mappings ?? [] as m (m.workspace + m.listId)}
      <div class="mapping">
        <h4>{m.workspace} ↔ {m.listTitle}</h4>
        {#if m.confirmed}
          <p class="muted">Syncing.</p>
        {:else}
          <p class="muted">Review what will happen before the first sync.</p>
          <div class="actions"><button class="btn" disabled={busy} onclick={() => preview(m.workspace)}>Review</button></div>
          {#if previews[m.workspace]}
            {@const p = previews[m.workspace]}
            <ul>
              <li>{p.createInGoogle} task{p.createInGoogle === 1 ? '' : 's'} will be created in Google</li>
              <li>{p.importToQuantos} will be added here from Google</li>
              <li>{p.link} already match and will be linked</li>
              <li>{p.excluded} left out (recurring, archived, or finished more than 30 days ago)</li>
            </ul>
            <div class="actions"><button class="btn primary" disabled={busy} onclick={() => confirm(m.workspace)}>Confirm and sync this list</button></div>
          {/if}
        {/if}
      </div>
    {/each}

    <div class="actions">
      <label class="inline">Sync
        <select class="input" value={status?.intervalSeconds} onchange={(e) => perform(() => api.put('google-tasks/settings', { intervalSeconds: Number((e.target as HTMLSelectElement).value) }))}>
          {#each intervalChoices as c (c.seconds)}<option value={c.seconds}>{c.label}</option>{/each}
          {#if status && !intervalChoices.some((c) => c.seconds === status?.intervalSeconds)}<option value={status.intervalSeconds}>Every {status.intervalSeconds} seconds</option>{/if}
        </select>
      </label>
      <button class="btn" disabled={busy || !status?.mappings.some((m) => m.confirmed)} onclick={() => perform(() => api.post('google-tasks/sync', {}))}>Sync now</button>
    </div>
    <p role="status" class="muted">{busy ? 'Syncing…' : syncLine(status)}</p>
    {#each status?.problems ?? [] as p, i (i)}<p class="error" role="alert">{p.workspace}: {p.reason}</p>{/each}

    {#if status?.pendingDeletes.length}
      <h3 tabindex="-1" bind:this={pendingHeading}>Deletions waiting</h3>
      <p>A lot of deletions were found at once, so nothing has been deleted yet.</p>
      <table>
        <thead><tr><th scope="col">Task</th><th scope="col">What will happen</th></tr></thead>
        <tbody>{#each status.pendingDeletes as d (d.taskId)}<tr><td>{d.title || 'Untitled task'}</td><td>{directionLabel(d.direction)}</td></tr>{/each}</tbody>
      </table>
      <div class="actions">
        <button class="btn primary" disabled={busy} onclick={() => resolveDeletes('apply')}>Apply</button>
        <button class="btn" disabled={busy} onclick={() => resolveDeletes('cancel')}>Cancel</button>
      </div>
    {/if}
  {/if}
</section>

<style>
  section { max-width: 760px; }
  p { margin: 12px 0; line-height: 1.55; }
  label { display: grid; gap: 8px; margin: 12px 0; font-weight: 600; }
  label.inline { display: flex; align-items: center; gap: 10px; margin: 0; }
  input, select { width: 100%; min-height: 44px; }
  .actions { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin: 16px 0; }
  .actions .btn, .rows .btn { min-height: 44px; }
  .muted { color: var(--text-muted); font-size: 14px; }
  .error { color: var(--danger); }
  .status { font-weight: 600; }
  details { margin: 16px 0; }
  summary { cursor: pointer; min-height: 44px; display: flex; align-items: center; font-weight: 600; }
  ol { padding-left: 22px; line-height: 1.6; }
  h3 { margin-top: 32px; }
  .rows { list-style: none; padding: 0; display: grid; gap: 12px; }
  .rows li { display: grid; gap: 4px; padding: 12px 0; border-bottom: 1px solid var(--line); }
  .mapping { margin: 16px 0; padding: 12px 0; border-top: 1px solid var(--line); }
  table { width: 100%; border-collapse: collapse; }
  th, td { text-align: left; padding: 8px 6px; border-bottom: 1px solid var(--line); overflow-wrap: anywhere; }
</style>
