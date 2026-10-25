<script lang="ts">
  import type { Tab } from '../../lib/layout.svelte';
  import { activeEditor } from '../../lib/activeEditor';
  import { app } from '../../lib/state.svelte';
  import { docs } from '../../lib/docs.svelte';
  import { openNode } from '../../lib/links.svelte';
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { isAndroidClient } from '../../lib/platform';

  /** PDF viewing and citations (BUILD-PLAN 8D.8): the browser's own viewer, plus "cite this page". */
  let { tab }: { tab: Tab } = $props();
  const [path, hash] = (tab.arg ?? '').split('#');
  let page = $state(Number(/page=(\d+)/.exec(hash ?? '')?.[1] ?? 1));
  let pages = $state(0);
  let pdfError = $state('');
  onMount(() => {
    if (isAndroidClient) api.get<{ pages: number }>(`android/pdf-info?path=${encodeURIComponent(path)}`)
      .then((r) => { pages = r.pages; }).catch((e) => { pdfError = e.message; });
  });
  $effect(() => { if (pages) page = Math.max(1, Math.min(pages, Math.floor(Number(page) || 1))); });
  const src = $derived(isAndroidClient ? `/api/android/pdf-page?path=${encodeURIComponent(path)}&page=${page}` : `/api/files/${path}#page=${page}`);
  const name = tab.title ?? 'PDF';

  /** The note's tab is closed or hidden: add the citation through the document store, where the caret was. */
  async function citeInto(id: string, md: string) {
    const doc = docs.acquire(id);
    try {
      while (doc.status === 'loading') await new Promise((r) => setTimeout(r, 30));
      const body = doc.body;
      let at = Math.min(activeEditor.lastPos ?? body.length, body.length);
      // Right after the caret, on the same line; a space keeps it apart from the word before.
      const insert = (at > 0 && !/\s/.test(body[at - 1]) ? ' ' : '') + md;
      doc.edit(body.slice(0, at) + insert + body.slice(at));
      activeEditor.lastPos = at + insert.length;
      await doc.flush();
      const title = doc.node?.title ?? 'the note';
      app.toast(`Citation added to “${title}”.`, 'ok', { label: 'Open', run: () => openNode(id, 'current', title) });
    } catch (e) { app.error(e); }
    finally { docs.release(id); }
  }

  function cite() {
    const md = `[${name}, p. ${page}](${path}#page=${page})`;
    if (activeEditor.view?.dom.isConnected) {
      const v = activeEditor.view;
      const at = v.state.selection.main.head;
      const before = at > 0 ? v.state.sliceDoc(at - 1, at) : '';
      const insert = (before && !/\s/.test(before) ? ' ' : '') + md;
      v.dispatch({ changes: { from: at, insert }, selection: { anchor: at + insert.length } });
      app.toast('Citation inserted into the last note you edited.', 'ok');
    } else if (activeEditor.docId) {
      void citeInto(activeEditor.docId, md);
    } else {
      navigator.clipboard.writeText(md).then(
        () => app.toast('Citation copied. Paste it into a note.', 'ok'),
        () => app.toast(`Couldn't copy. The citation is: ${md}`, 'warn'));
    }
  }
</script>

<div class="pdf">
  <div class="bar">
    <span class="name" title={path}>{name}</span>
    <label>Page <input class="input" type="number" min="1" max={pages || undefined} bind:value={page} /></label>
    {#if isAndroidClient && pages}<span>of {pages}</span>{/if}
    <button class="btn" onclick={cite}>Cite this page</button>
  </div>
  {#if isAndroidClient}
    {#if pdfError}<p role="alert">{pdfError}</p>
    {:else if pages}
      <div class="native-page"><img {src} alt={`Page ${page} of ${name}`} onerror={() => { pdfError = 'This PDF page could not be opened.'; }} /></div>
      <div class="page-actions"><button class="btn" disabled={page <= 1} onclick={() => page--}>Previous page</button><button class="btn" disabled={page >= pages} onclick={() => page++}>Next page</button></div>
    {:else}<p role="status">Opening PDF…</p>{/if}
  {:else}{#key src}<iframe title={name} {src}></iframe>{/key}{/if}
</div>

<style>
  .pdf { flex: 1; display: flex; flex-direction: column; min-height: 0; }
  .bar { display: flex; gap: 12px; align-items: center; padding: 8px 14px; border-bottom: 1px solid var(--line); }
  .bar .name { flex: 1; font-size: 13.5px; color: var(--text-soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .bar label { display: flex; gap: 6px; align-items: center; font-size: 13px; }
  .bar .input { width: 70px; height: 28px; }
  iframe { flex: 1; border: 0; background: #525659; }
  .native-page { flex: 1; min-height: 0; overflow: auto; background: #525659; padding: 8px; }
  .native-page img { width: 100%; height: auto; display: block; }
  .page-actions { display: flex; justify-content: space-between; gap: 12px; padding: 8px; }
  @media (max-width: 700px) { .bar { flex-wrap: wrap; gap: 8px; } .bar .name { flex-basis: 100%; } }
</style>
