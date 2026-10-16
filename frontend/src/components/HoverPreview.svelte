<script lang="ts">
  import { api } from '../lib/api';
  import { hover, ULID, openTarget } from '../lib/links.svelte';
  import { renderMarkdown } from '../lib/render';
  import type { NodeDto } from '../lib/types';

  let node = $state<NodeDto | null>(null);
  let loadedFor = '';

  $effect(() => {
    const target = hover.target;
    if (!target || !ULID.test(target)) { node = null; return; }
    if (target === loadedFor) return;
    loadedFor = target;
    api.get<NodeDto>(`nodes/${target}`).then((n) => { if (hover.target === target) node = n; }).catch(() => (node = null));
  });

  const style = $derived.by(() => {
    const r = hover.rect;
    if (!r) return '';
    const below = r.bottom + 320 < window.innerHeight;
    const left = Math.min(Math.max(8, r.left), window.innerWidth - 440);
    return below ? `left:${left}px; top:${r.bottom + 6}px` : `left:${left}px; bottom:${window.innerHeight - r.top + 6}px`;
  });
</script>

{#if hover.target && node}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="preview" style={style} onmouseenter={() => hover.keep()} onmouseleave={() => hover.hide()}>
    <button class="title" onclick={(e) => { openTarget(node!.id, e.shiftKey ? 'other' : 'new'); hover.hide(); }}>{node.title}</button>
    <div class="body">{@html renderMarkdown(node.body.slice(0, 2500), { links: false })}</div>
  </div>
{/if}

<style>
  .preview { position: fixed; z-index: 70; width: 420px; max-height: 320px; overflow: hidden; border-radius: 10px; background: var(--bg-raised); border: 1px solid var(--line-strong); box-shadow: var(--shadow); padding: 12px 14px; }
  .title { display: block; border: 0; background: none; padding: 0; font: 700 15px/1.3 var(--font-display); color: var(--accent); margin-bottom: 6px; text-align: left; }
  .body { font-size: 13.5px; color: var(--text-soft); max-height: 260px; overflow: hidden; mask-image: linear-gradient(#000 80%, transparent); }
  .body :global(h1), .body :global(h2), .body :global(h3) { font-size: 14px; margin: 8px 0 4px; }
  .body :global(p) { margin: 4px 0; }
  .body :global(ul) { padding-left: 18px; margin: 4px 0; }
  .body :global(code) { font-size: 12px; }
</style>
