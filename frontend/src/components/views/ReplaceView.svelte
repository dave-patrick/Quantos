<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import Icon from '../Icon.svelte';

  /** Batch find and replace (Release 1.1): preview every change, choose the notes, undo the lot. */
  interface Sample { line: number; before: string; after: string }
  interface Hit { id: string; title: string; path: string; count: number; locked: boolean; samples: Sample[] }
  interface Preview { notes: Hit[]; total: number; truncated: boolean }

  let find = $state('');
  let replace = $state('');
  let matchCase = $state(false);
  let wholeWord = $state(false);
  let regex = $state(false);
  let everywhere = $state(false);
  let preview = $state<Preview | null>(null);
  let problem = $state('');
  let chosen = $state<Record<string, boolean>>({});
  let busy = $state(false);
  let box = $state<HTMLInputElement | null>(null);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let seq = 0;

  onMount(() => box?.focus());

  const query = () => ({ find, replace, regex, matchCase, wholeWord, workspace: everywhere ? null : app.workspace });

  // Previews follow the typing, a moment after it stops.
  $effect(() => {
    void [find, replace, matchCase, wholeWord, regex, everywhere];
    clearTimeout(timer);
    if (!find) { preview = null; problem = ''; return; }
    timer = setTimeout(load, 300);
  });

  async function load() {
    const mine = ++seq;
    try {
      const p = await api.post<Preview>('replace/preview', query());
      if (mine !== seq) return;
      preview = p; problem = '';
      chosen = Object.fromEntries(p.notes.map((n) => [n.id, chosen[n.id] ?? !n.locked]));
    } catch (e) {
      if (mine === seq) { preview = null; problem = (e as Error).message; }
    }
  }

  const picked = $derived(preview?.notes.filter((n) => chosen[n.id] && !n.locked) ?? []);
  const pickedCount = $derived(picked.reduce((s, n) => s + n.count, 0));
  const allOn = $derived(!!preview && preview.notes.filter((n) => !n.locked).every((n) => chosen[n.id]));

  function toggleAll() {
    const on = !allOn;
    chosen = Object.fromEntries((preview?.notes ?? []).map((n) => [n.id, on && !n.locked]));
  }

  async function apply() {
    if (!picked.length) return;
    const ok = await app.confirm('Replace across notes?',
      `${pickedCount} ${pickedCount === 1 ? 'change' : 'changes'} in ${picked.length} ${picked.length === 1 ? 'note' : 'notes'}. Each note keeps its current text in its history, and you can undo the whole replace straight after.`, 'Replace');
    if (!ok) return;
    busy = true;
    try {
      const r = await api.post<{ undoId: string | null; notes: number; replacements: number; skipped: string[] }>('replace/apply', { ...query(), ids: picked.map((n) => n.id) });
      const skipped = r.skipped.length ? ` Skipped: ${r.skipped.slice(0, 3).join(', ')}${r.skipped.length > 3 ? '…' : ''}.` : '';
      app.toast(`Replaced ${r.replacements} in ${r.notes} ${r.notes === 1 ? 'note' : 'notes'}.${skipped}`, 'ok',
        r.undoId ? { label: 'Undo', run: () => undo(r.undoId!) } : undefined);
      await load();
    } catch (e) { app.error(e); }
    finally { busy = false; }
  }

  async function undo(id: string) {
    try {
      const r = await api.post<{ restored: number; kept: string[] }>(`replace/undo/${id}`);
      app.toast(`Put back ${r.restored} ${r.restored === 1 ? 'note' : 'notes'}.` + (r.kept.length ? ` Left alone (edited since): ${r.kept.join(', ')}.` : ''), 'ok');
      await load();
    } catch (e) { app.error(e); }
  }
</script>

<div class="scroll replace">
  <div class="inner">
    <h1><Icon name="replace" size={22} /> Find and replace</h1>
    <p class="faint intro">Changes the text of notes, never their properties, links or task markers (rename a note to change its links). Locked notes are left alone.</p>

    <div class="form">
      <input class="input" bind:this={box} bind:value={find} placeholder="Find" aria-label="Find" spellcheck="false" />
      <input class="input" bind:value={replace} placeholder="Replace with" aria-label="Replace with" spellcheck="false"
        onkeydown={(e) => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) apply(); }} />
      <div class="opts">
        <label class="check"><input type="checkbox" bind:checked={matchCase} /> Match case</label>
        <label class="check"><input type="checkbox" bind:checked={wholeWord} /> Whole words</label>
        <label class="check" title="Regular expression; use $1 in the replacement for a captured group"><input type="checkbox" bind:checked={regex} /> Pattern (regex)</label>
        <label class="check"><input type="checkbox" bind:checked={everywhere} /> All workspaces</label>
      </div>
    </div>

    {#if problem}<p class="warn">{problem}</p>{/if}

    {#if preview}
      <div class="summary">
        {#if preview.notes.length}
          <label class="check"><input type="checkbox" checked={allOn} onchange={toggleAll} /> {preview.total} {preview.total === 1 ? 'match' : 'matches'} in {preview.notes.length}{preview.truncated ? '+' : ''} {preview.notes.length === 1 ? 'note' : 'notes'}</label>
          <button class="btn primary" disabled={busy || !picked.length} onclick={apply}>{busy ? 'Replacing…' : `Replace in ${picked.length} ${picked.length === 1 ? 'note' : 'notes'}`}</button>
        {:else}
          <span class="faint">No matches.</span>
        {/if}
      </div>
      <ul class="hits">
        {#each preview.notes as n (n.id)}
          <li class:off={!chosen[n.id]}>
            <div class="head">
              <input type="checkbox" aria-label={`Replace in ${n.title}`} disabled={n.locked} bind:checked={chosen[n.id]} />
              <button class="title" onclick={(e) => openNode(n.id, whereFrom(e) === 'current' ? 'new' : whereFrom(e), n.title)}>{n.title}</button>
              {#if n.locked}<span class="faint lock"><Icon name="lock" size={12} /> locked</span>{/if}
              <span class="faint count">{n.count}</span>
            </div>
            {#each n.samples as s, i (i)}
              <div class="sample"><span class="ln faint">{s.line || ''}</span><div><div class="before">{s.before}</div><div class="after">{s.after}</div></div></div>
            {/each}
          </li>
        {/each}
      </ul>
    {:else if !find}
      <p class="faint hint">Type what to find. Matches show here with the change before anything is replaced.</p>
    {/if}
  </div>
</div>

<style>
  .replace { flex: 1; }
  .inner { max-width: 860px; margin: 0 auto; padding: 24px 28px 40px; }
  h1 { display: flex; align-items: center; gap: 10px; font: 700 28px/1.2 var(--font-display); }
  .intro { font-size: 13.5px; margin: 6px 0 16px; }
  .form { display: grid; gap: 8px; }
  .opts { display: flex; flex-wrap: wrap; gap: 6px 18px; }
  .check { display: inline-flex; gap: 6px; align-items: center; font-size: 13px; color: var(--text-muted); }
  .warn { color: var(--warn); font-size: 13.5px; margin-top: 10px; }
  .summary { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin: 18px 0 8px; padding-bottom: 8px; border-bottom: 1px solid var(--line); }
  .hits { list-style: none; padding: 0; margin: 0; }
  .hits li { padding: 8px 0; border-bottom: 1px solid var(--line); }
  .hits li.off .sample { opacity: .45; }
  .head { display: flex; align-items: center; gap: 8px; }
  .title { border: 0; background: none; color: var(--text); font: 600 14.5px var(--font-body); padding: 0; cursor: pointer; text-align: left; }
  .title:hover { color: var(--accent); text-decoration: underline; }
  .count { margin-left: auto; font: 12px var(--font-mono); }
  .lock { display: inline-flex; gap: 4px; align-items: center; font-size: 12px; }
  .sample { display: grid; grid-template-columns: 34px 1fr; gap: 6px; margin: 6px 0 0 26px; font: 12.5px/1.5 var(--font-mono); }
  .ln { text-align: right; }
  .before, .after { white-space: pre-wrap; word-break: break-word; padding: 1px 6px; border-radius: 3px; }
  .before { background: var(--danger-wash); color: var(--text-soft); text-decoration: line-through; text-decoration-color: color-mix(in srgb, var(--danger) 60%, transparent); }
  .after { background: var(--ok-wash); color: var(--text); }
  .hint { font-size: 14px; margin-top: 18px; }
</style>
