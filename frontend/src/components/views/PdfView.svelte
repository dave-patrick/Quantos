<script lang="ts">
  import type { Tab } from '../../lib/layout.svelte';
  import { activeEditor } from '../../lib/activeEditor';
  import { app } from '../../lib/state.svelte';

  /** PDF viewing and citations (BUILD-PLAN 8D.8): the browser's own viewer, plus "cite this page". */
  let { tab }: { tab: Tab } = $props();
  const [path, hash] = (tab.arg ?? '').split('#');
  let page = $state(Number(/page=(\d+)/.exec(hash ?? '')?.[1] ?? 1));
  const src = $derived(`/api/files/${path}#page=${page}`);
  const name = tab.title ?? 'PDF';

  function cite() {
    const md = `[${name}, p. ${page}](${path}#page=${page})`;
    if (activeEditor.view) {
      const v = activeEditor.view;
      v.dispatch({ changes: { from: v.state.selection.main.head, insert: md } });
      app.toast('Citation inserted into the last note you edited.', 'ok');
    } else {
      navigator.clipboard.writeText(md);
      app.toast('Citation copied. Paste it into a note.', 'ok');
    }
  }
</script>

<div class="pdf">
  <div class="bar">
    <span class="mono faint">{path}</span>
    <label>Page <input class="input" type="number" min="1" bind:value={page} /></label>
    <button class="btn" onclick={cite}>Cite this page</button>
  </div>
  {#key src}<iframe title={name} {src}></iframe>{/key}
</div>

<style>
  .pdf { flex: 1; display: flex; flex-direction: column; min-height: 0; }
  .bar { display: flex; gap: 12px; align-items: center; padding: 8px 14px; border-bottom: 1px solid var(--line); }
  .bar .mono { flex: 1; font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .bar label { display: flex; gap: 6px; align-items: center; font-size: 13px; }
  .bar .input { width: 70px; height: 28px; }
  iframe { flex: 1; border: 0; background: #525659; }
</style>
