<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import { layout } from '../lib/layout.svelte';
  import { onChange } from '../lib/events';
  import { openNode, openTarget, whereFrom } from '../lib/links.svelte';
  import { app } from '../lib/state.svelte';
  import type { LinkRef } from '../lib/types';
  import Icon from './Icon.svelte';

  const id = $derived(layout.activeTab?.nodeId ?? null);
  let backlinks = $state<LinkRef[]>([]);
  let outlinks = $state<LinkRef[]>([]);
  let filter = $state('');
  let scope = $state<'all' | 'workspace'>('all');
  let kind = $state<'all' | 'link' | 'embed'>('all');
  let timer: ReturnType<typeof setTimeout>;

  async function load(target: string | null) {
    if (!target) { backlinks = []; outlinks = []; return; }
    [backlinks, outlinks] = await Promise.all([api.get<LinkRef[]>(`nodes/${target}/backlinks`), api.get<LinkRef[]>(`nodes/${target}/outlinks`)]);
  }

  $effect(() => { load(id).catch(() => {}); });
  onMount(() => onChange(() => { clearTimeout(timer); timer = setTimeout(() => load(id).catch(() => {}), 400); }));

  // Backlink filtering (BUILD-PLAN 8C.14): by text, workspace and link kind.
  const visible = $derived(backlinks.filter((b) =>
    (scope === 'all' || b.sourceWorkspace === app.workspace) &&
    (kind === 'all' || b.kind === kind) &&
    (!filter.trim() || (b.sourceTitle + ' ' + b.context).toLowerCase().includes(filter.trim().toLowerCase()))));
  const grouped = $derived.by(() => {
    const m = new Map<string, LinkRef[]>();
    for (const b of visible) m.set(b.sourceId, [...(m.get(b.sourceId) ?? []), b]);
    return [...m.values()];
  });
  // Each linked note once, however many times this note mentions it.
  const unique = (list: LinkRef[]) => {
    const m = new Map<string, { link: LinkRef; count: number }>();
    for (const o of list) { const k = o.target + '|' + o.kind; const e = m.get(k); if (e) e.count++; else m.set(k, { link: o, count: 1 }); }
    return [...m.values()];
  };
  const unresolved = $derived(unique(outlinks.filter((o) => o.target.startsWith('?'))));
  const resolved = $derived(unique(outlinks.filter((o) => !o.target.startsWith('?'))));
  const shownContexts = 5;

  const clean = (s: string) => s.replace(/\[\[[0-9A-HJKMNP-TV-Z]{26}(?:#[^\]|]+)?\|([^\]]+)\]\]/g, '$1').replace(/<!--task:[0-9A-Z]{26}-->/g, '');
</script>

<div class="scroll links">
  {#if !id}
    <p class="faint pad">Open a note to see what links to it.</p>
  {:else}
    <section>
      <h3 class="label">Backlinks <span class="faint" title="{visible.length} link{visible.length === 1 ? '' : 's'}">{grouped.length}</span></h3>
      <div class="filters">
        <input class="input" placeholder="Filter backlinks" bind:value={filter} />
        <select class="input" bind:value={scope}><option value="all">All workspaces</option><option value="workspace">This workspace</option></select>
        <select class="input" bind:value={kind}><option value="all">Links and embeds</option><option value="link">Links</option><option value="embed">Embeds</option></select>
      </div>
      {#each grouped as group (group[0].sourceId)}
        <div class="ref">
          <button class="src" onclick={(e) => openNode(group[0].sourceId, whereFrom(e), group[0].sourceTitle)}>
            <Icon name="file-text" size={13} /> {group[0].sourceTitle} <span class="faint">{group[0].sourceWorkspace !== app.workspace ? '· ' + group[0].sourceWorkspace : ''}</span>
          </button>
          {#each group.slice(0, shownContexts) as b, i (i)}<p class="ctx">{clean(b.context)}</p>{/each}
          {#if group.length > shownContexts}<p class="ctx faint">+{group.length - shownContexts} more</p>{/if}
        </div>
      {:else}
        <p class="faint">Nothing links here yet.</p>
      {/each}
    </section>
    <section>
      <h3 class="label">Forward links <span class="faint">{resolved.length}</span></h3>
      {#each resolved as { link: o, count }, i (i)}
        <button class="src" onclick={(e) => openNode(o.target, whereFrom(e), o.sourceTitle)}><Icon name={o.kind === 'embed' ? 'layers' : 'link'} size={13} /> {o.sourceTitle}{#if count > 1}<span class="faint count">×{count}</span>{/if}</button>
      {:else}<p class="faint">No links in this note.</p>{/each}
      {#if unresolved.length}
        <h3 class="label">Unresolved</h3>
        {#each unresolved as { link: o }, i (i)}
          <button class="src dashed" title="Open or create" onclick={(e) => openTarget(o.target.slice(1), whereFrom(e))}><Icon name="plus" size={13} /> {o.target.slice(1)}</button>
        {/each}
      {/if}
    </section>
  {/if}
</div>

<style>
  .links { flex: 1; padding: 10px; }
  section { margin-bottom: 18px; }
  h3 { display: flex; gap: 6px; margin: 0 0 8px; }
  .filters { display: grid; gap: 4px; margin-bottom: 8px; }
  .filters .input { height: 28px; font-size: 12.5px; }
  .ref { padding: 6px 0; border-bottom: 1px solid var(--line); }
  .count { margin-left: auto; font: 11px var(--font-mono); }
  .src { display: flex; align-items: center; gap: 6px; width: 100%; text-align: left; border: 0; background: none; color: var(--accent); padding: 4px 6px; border-radius: 5px; font-size: 13.5px; }
  .src:hover { background: var(--bg-hover); }
  .src.dashed { color: var(--text-muted); }
  .ctx { margin: 2px 6px 4px 25px; font-size: 12.5px; color: var(--text-muted); overflow-wrap: anywhere; }
  .pad { padding: 8px; }
</style>
