<script lang="ts">
  import { diffComm } from 'node-diff3';
  import type { Doc } from '../lib/docs.svelte';
  import { app } from '../lib/state.svelte';
  import Icon from './Icon.svelte';

  let { doc }: { doc: Doc } = $props();
  let busy = $state(false);

  const conflict = $derived(doc.conflict!);
  const disk = $derived(doc.raw ? conflict.disk.content : conflict.disk.body);
  const rows = $derived.by(() => {
    const blocks = diffComm(conflict.mine.split('\n'), disk.split('\n'));
    const out: { left: string[]; right: string[]; same: boolean }[] = [];
    for (const b of blocks) {
      if (b.common) out.push({ left: b.common, right: b.common, same: true });
      else out.push({ left: b.buffer1 ?? [], right: b.buffer2 ?? [], same: false });
    }
    return out;
  });

  async function choose(choice: 'mine' | 'disk') {
    busy = true;
    try {
      await doc.resolve(choice);
      app.toast(choice === 'mine' ? 'Kept this version. The disk version is saved in history.' : 'Kept the disk version. Your version is saved in history.', 'ok');
    } catch (e) { app.error(e); }
    finally { busy = false; }
  }
</script>

<section class="conflict">
  <header>
    <Icon name="alert" size={18} />
    <div>
      <h2>This note changed in two places</h2>
      <p>{conflict.reason} Choose which version to keep. The other one is saved as a recoverable copy in version history, so nothing is lost.</p>
    </div>
  </header>
  <div class="cols">
    <div class="col-head"><span class="label">This window</span><button class="btn primary" disabled={busy} onclick={() => choose('mine')}>Keep this version</button></div>
    <div class="col-head"><span class="label">On disk</span><button class="btn" disabled={busy} onclick={() => choose('disk')}>Keep disk version</button></div>
    {#each rows as row, i (i)}
      <pre class="cell" class:changed={!row.same} class:mine={!row.same}>{row.left.join('\n') || (row.same ? '' : ' ')}</pre>
      <pre class="cell" class:changed={!row.same} class:theirs={!row.same}>{row.right.join('\n') || (row.same ? '' : ' ')}</pre>
    {/each}
  </div>
</section>

<style>
  .conflict { max-width: 1200px; margin: 16px auto; }
  header { display: flex; gap: 12px; padding: 14px 16px; border-radius: 10px; background: var(--warn-wash); color: var(--warn); border: 1px solid color-mix(in srgb, var(--warn) 40%, transparent); margin-bottom: 14px; }
  h2 { font: 700 17px/1.3 var(--font-display); color: var(--text); }
  header p { margin: 4px 0 0; color: var(--text-soft); font-size: 14px; }
  .cols { display: grid; grid-template-columns: 1fr 1fr; border: 1px solid var(--line-strong); border-radius: 10px; overflow: hidden; }
  .col-head { display: flex; align-items: center; justify-content: space-between; padding: 8px 12px; background: var(--bg-raised); border-bottom: 1px solid var(--line-strong); position: sticky; top: 0; }
  .col-head + .col-head { border-left: 1px solid var(--line-strong); }
  .cell { margin: 0; padding: 4px 12px; font: 13px/1.6 var(--font-mono); white-space: pre-wrap; overflow-wrap: anywhere; color: var(--text-soft); min-height: 4px; }
  .cell:nth-child(even) { border-left: 1px solid var(--line-strong); }
  .cell.mine { background: color-mix(in srgb, var(--accent) 12%, transparent); color: var(--text); }
  .cell.theirs { background: color-mix(in srgb, var(--warn) 12%, transparent); color: var(--text); }
</style>
