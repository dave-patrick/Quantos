<script lang="ts">
  import { tick, untrack } from 'svelte';
  import type { Tab } from '../../lib/layout.svelte';
  import { layout } from '../../lib/layout.svelte';
  import { commandList, displayKeys, keysFor } from '../../lib/commands.svelte';
  import { renderMarkdown } from '../../lib/render';
  import Icon from '../Icon.svelte';
  // The guides ship inside the app, so Help works offline and always matches this version.
  import guide from '../../../../documentation/USER-GUIDE.md?raw';
  import troubleshooting from '../../../../documentation/TROUBLESHOOTING.md?raw';
  import cli from '../../../../documentation/CLI-REFERENCE.md?raw';
  import agents from '../../../../documentation/AGENT-ADAPTERS.md?raw';
  import plugins from '../../../../documentation/PLUGINS.md?raw';

  /** Help: the user guide and the other guides, searchable, plus the live list of keyboard shortcuts. */
  let { tab }: { tab: Tab } = $props();

  const DOCS = [
    { key: 'guide', label: 'User guide', icon: 'book', text: guide },
    { key: 'shortcuts', label: 'Keyboard shortcuts', icon: 'command', text: '' },
    { key: 'troubleshooting', label: 'Troubleshooting', icon: 'alert', text: troubleshooting },
    { key: 'cli', label: 'Command line', icon: 'terminal', text: cli },
    { key: 'agents', label: 'AI agents', icon: 'sparkles', text: agents },
    { key: 'plugins', label: 'Plugins', icon: 'zap', text: plugins }
  ];
  let doc = $state(untrack(() => tab.arg) && DOCS.some((d) => d.key === untrack(() => tab.arg)) ? untrack(() => tab.arg)! : 'guide');
  let query = $state('');
  let body = $state<HTMLElement | null>(null);

  const slug = (s: string) => s.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '');

  /** A guide split at its ## and ### headings, for the contents list and for search. */
  function sections(text: string) {
    const out: { level: number; title: string; text: string }[] = [];
    let current = { level: 1, title: '', text: '' };
    let fence = false;
    for (const line of text.split(/\r?\n/)) {
      if (/^\s*```/.test(line)) fence = !fence;
      const h = !fence && /^(#{2,3})\s+(.*)$/.exec(line);
      if (h) { out.push(current); current = { level: h[1].length, title: h[2].trim(), text: line + '\n' }; }
      else current.text += line + '\n';
    }
    out.push(current);
    return out.filter((s) => s.text.trim());
  }

  // Keyboard shortcuts, read from the commands themselves (including any you've changed).
  const shortcutsMd = $derived.by(() => {
    const groups = new Map<string, string[]>();
    for (const c of commandList.items) {
      const k = keysFor(c.id);
      if (!k) continue;
      const list = groups.get(c.group) ?? [];
      list.push(`| ${c.title} | \`${displayKeys(k)}\` |`);
      groups.set(c.group, list);
    }
    return '# Keyboard shortcuts\n\nThe command palette (`' + displayKeys(keysFor('palette.commands')) + '`) lists every command, with or without a shortcut. Change shortcuts in *Settings › Shortcuts*.\n\n'
      + [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([g, rows]) => `## ${g}\n\n| Command | Keys |\n|---|---|\n${rows.sort().join('\n')}\n`).join('\n');
  });

  const current = $derived(DOCS.find((d) => d.key === doc)!);
  const text = $derived(doc === 'shortcuts' ? shortcutsMd : current.text);
  const toc = $derived(sections(text).filter((s) => s.title));

  // Searching looks through every guide and shows the sections that contain all the words.
  const hits = $derived.by(() => {
    const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    if (!words.length) return null;
    return DOCS.flatMap((d) => sections(d.key === 'shortcuts' ? shortcutsMd : d.text).filter((s) => words.every((w) => s.text.toLowerCase().includes(w))).map((s) => ({ doc: d, ...s }))).slice(0, 40);
  });

  // Headings get ids so the contents list can jump to them.
  $effect(() => {
    void text; void hits;
    tick().then(() => body?.querySelectorAll('h1, h2, h3').forEach((h) => { if (!h.id) h.id = 'help-' + slug(h.textContent ?? ''); }));
  });

  function go(title: string) {
    body?.querySelector('#help-' + CSS.escape(slug(title)))?.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }
  function pick(key: string) {
    doc = key; query = '';
    tab.arg = key; layout.persist();
    body?.scrollTo({ top: 0 });
  }
  async function openHit(key: string, title: string) {
    pick(key);
    await tick(); await tick();
    go(title);
  }
</script>

<div class="help">
  <aside class="side scroll">
    <h1><Icon name="help" size={20} /> Help</h1>
    <input class="input" aria-label="Search help" placeholder="Search help" bind:value={query} />
    <nav aria-label="Guides">
      {#each DOCS as d (d.key)}
        <button class="doc" class:on={doc === d.key && !hits} aria-current={doc === d.key && !hits ? 'page' : undefined} onclick={() => pick(d.key)}><Icon name={d.icon} size={14} /> {d.label}</button>
        {#if doc === d.key && !hits}
          <div class="toc">
            {#each toc as s, i (i)}<button class="h{s.level}" onclick={() => go(s.title)}>{s.title.replace(/[*`]/g, '')}</button>{/each}
          </div>
        {/if}
      {/each}
    </nav>
  </aside>

  <section class="content scroll" bind:this={body}>
    {#if hits}
      <p class="faint count">{hits.length ? `${hits.length} ${hits.length === 1 ? 'section' : 'sections'} mention “${query.trim()}”` : `Nothing in the guides mentions “${query.trim()}”.`}</p>
      {#each hits as h, i (i)}
        <article class="hit">
          <button class="where" onclick={() => openHit(h.doc.key, h.title)}>{h.doc.label}{h.title ? ' › ' + h.title.replace(/[*`]/g, '') : ''} <Icon name="arrow-right" size={12} /></button>
          <div class="rendered md">{@html renderMarkdown(h.text)}</div>
        </article>
      {/each}
    {:else}
      <div class="rendered md">{@html renderMarkdown(text)}</div>
    {/if}
  </section>
</div>

<style>
  .help { flex: 1; display: grid; grid-template-columns: 260px 1fr; min-height: 0; }
  .side { border-right: 1px solid var(--line); padding: 16px 12px; display: flex; flex-direction: column; gap: 10px; }
  h1 { display: flex; align-items: center; gap: 8px; font: 700 24px/1.2 var(--font-display); }
  nav { display: grid; gap: 2px; }
  .doc { display: flex; align-items: center; gap: 8px; border: 0; background: none; color: var(--text-soft); font: 600 14px var(--font-body); padding: 7px 8px; border-radius: 6px; text-align: left; cursor: pointer; }
  .doc:hover { background: var(--bg-hover); }
  .doc.on { background: var(--bg-active); color: var(--accent-strong); }
  .toc { display: grid; margin: 2px 0 8px 12px; border-left: 1px solid var(--line); }
  .toc button { border: 0; background: none; color: var(--text-muted); font: 13px var(--font-body); text-align: left; padding: 3px 10px; cursor: pointer; }
  .toc button.h3 { padding-left: 22px; font-size: 12.5px; }
  .toc button:hover { color: var(--accent); }
  .content { padding: 22px 36px 60px; }
  .md { max-width: 820px; font-size: 15px; line-height: 1.65; }
  .md :global(h1) { font: 700 30px/1.2 var(--font-display); margin: 0 0 14px; }
  .md :global(h2) { font: 700 21px/1.25 var(--font-display); margin: 30px 0 10px; padding-top: 8px; border-top: 1px solid var(--line); }
  .md :global(h3) { font: 700 16.5px/1.3 var(--font-display); margin: 20px 0 8px; }
  .md :global(table) { border-collapse: collapse; margin: 10px 0 16px; font-size: 14px; }
  .md :global(th), .md :global(td) { border-bottom: 1px solid var(--line); padding: 5px 12px 5px 0; text-align: left; }
  .md :global(code) { font: 0.88em var(--font-mono); background: var(--bg-raised); padding: 0.05em 0.35em; border-radius: 4px; }
  .md :global(pre) { background: var(--bg-raised); border: 1px solid var(--line); border-radius: 8px; padding: 10px 12px; overflow: auto; }
  .md :global(pre code) { background: none; padding: 0; }
  .md :global(hr) { border: 0; border-top: 1px solid var(--line); margin: 24px 0; }
  .count { font-size: 13px; }
  .hit { border-bottom: 1px solid var(--line); padding: 10px 0; max-width: 820px; }
  .hit .md :global(h2), .hit .md :global(h3) { border: 0; margin-top: 0; padding-top: 0; }
  .where { display: inline-flex; align-items: center; gap: 6px; border: 0; background: none; color: var(--accent); font: 600 13px var(--font-body); padding: 0; margin-bottom: 4px; cursor: pointer; }
  @container pane (max-width: 760px) {
    .help { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
    .side { border-right: 0; border-bottom: 1px solid var(--line); max-height: 220px; }
  }
</style>
