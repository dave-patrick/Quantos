<script lang="ts">
  import { app } from '../lib/state.svelte';
  import { layout, type TabKind } from '../lib/layout.svelte';
  import { whereFrom } from '../lib/links.svelte';
  import { views } from '../lib/views.svelte';
  import { supertags } from '../lib/supertags.svelte';
  import { isWindowsClient } from '../lib/platform';
  import { terminal } from '../lib/terminal.svelte';
  import { capture } from '../lib/capture.svelte';
  import Icon from './Icon.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { api } from '../lib/api';
  import { onChange } from '../lib/events';
  import type { TreeFolder } from '../lib/types';

  // Folder navigation (BUILD-PLAN section 7): the workspace's own folders, plus saved views marked as folders.
  let folders = $state<{ name: string; path: string; depth: number }[]>([]);
  async function loadFolders() {
    try {
      const tree = await api.get<TreeFolder>(`tree?workspace=${app.workspace}`);
      const out: { name: string; path: string; depth: number }[] = [];
      const builtIn = ['daily', 'tasks', 'archive', 'supertags'];
      const add = (f: TreeFolder, depth: number) => { out.push({ name: f.name, path: f.path, depth }); f.folders.forEach((c) => add(c, depth + 1)); };
      for (const top of tree.folders) {
        if (top.name === 'nodes') top.folders.forEach((c) => add(c, 0));   // folders inside Notes
        else if (!builtIn.includes(top.name)) add(top, 0);                // custom top-level folders
      }
      folders = out;
    } catch { folders = []; }
  }
  $effect(() => { void app.workspace; loadFolders(); });
  onChange((e) => { if (e.kind === 'Moved' || e.kind === 'Created') loadFolders(); });

  const nav: { kind: TabKind; label: string; icon: string }[] = [
    { kind: 'notes', label: 'Notes', icon: 'file-text' },
    { kind: 'daily', label: 'Daily stream', icon: 'calendar' },
    { kind: 'myday', label: 'My Day', icon: 'sun' },
    { kind: 'tasks', label: 'Tasks', icon: 'check-square' },
    { kind: 'timeline', label: 'Timeline', icon: 'clock' },
    { kind: 'gantt', label: 'Gantt', icon: 'gantt' },
    { kind: 'review', label: 'Stale tasks', icon: 'history' },
    { kind: 'sprints', label: 'Sprints', icon: 'layers' },
    { kind: 'goals', label: 'Goals', icon: 'target' },
    { kind: 'focus', label: 'Focus', icon: 'timer' },
    { kind: 'habits', label: 'Habits', icon: 'activity' },
    { kind: 'search', label: 'Search', icon: 'search' },
    { kind: 'ask', label: 'Ask your notes', icon: 'sparkles' },
    { kind: 'graph', label: 'Graph', icon: 'graph' },
    { kind: 'map', label: 'Map', icon: 'map' }
  ];

  const tools: { kind: TabKind; label: string; icon: string }[] = [
    { kind: 'templates', label: 'Templates', icon: 'copy' },
    { kind: 'dictionary', label: 'Metadata dictionary', icon: 'book' },
    { kind: 'diagnostics', label: 'Vault diagnostics', icon: 'activity' },
    { kind: 'attachments', label: 'Attachment auditor', icon: 'paperclip' },
    { kind: 'trash', label: 'Trash', icon: 'trash' }
  ];

  // The top list can be reordered and trimmed, and whole sections hidden (per device, in prefs).
  type NavItem = { key: string; label: string; icon: string; kind?: TabKind };
  const allItems: NavItem[] = [...nav.map((n) => ({ key: n.kind, ...n })), { key: 'capture', label: 'Quick capture', icon: 'inbox' }, { key: 'terminal', label: 'Terminal', icon: 'terminal' }];
  const sections = [{ key: 'pinned', label: 'Pinned views' }, { key: 'folders', label: 'Folders' }, { key: 'queries', label: 'Saved queries' }, { key: 'tags', label: 'Supertags' }, { key: 'tools', label: 'Vault' }];
  const ordered = $derived.by(() => {
    const rank = (k: string) => { const i = prefs.sidebarOrder.indexOf(k); return i < 0 ? 1000 + allItems.findIndex((x) => x.key === k) : i; };
    return [...allItems].sort((a, b) => rank(a.key) - rank(b.key));
  });
  const shownItems = $derived(ordered.filter((i) => !prefs.sidebarHidden.includes(i.key)));
  const showSection = (k: string) => !prefs.sidebarHidden.includes('section:' + k);
  let customizing = $state(false);

  function toggle(key: string) {
    prefs.sidebarHidden = prefs.sidebarHidden.includes(key) ? prefs.sidebarHidden.filter((k) => k !== key) : [...prefs.sidebarHidden, key];
  }
  function move(key: string, by: number) {
    const keys = ordered.map((i) => i.key);
    const i = keys.indexOf(key), j = i + by;
    if (j < 0 || j >= keys.length) return;
    [keys[i], keys[j]] = [keys[j], keys[i]];
    prefs.sidebarOrder = keys;
  }
  function reset() { prefs.sidebarHidden = []; prefs.sidebarOrder = []; prefs.sidebarSections = []; }

  // Drag and drop: items reorder among items, sections among sections (the arrows in Customise do the same by keyboard).
  const SECTION_KEYS = ['pinned', 'folders', 'queries', 'tags', 'tools'];
  const sectionOrder = $derived([...SECTION_KEYS].sort((a, b) => {
    const r = (k: string) => { const i = prefs.sidebarSections.indexOf(k); return i < 0 ? 100 + SECTION_KEYS.indexOf(k) : i; };
    return r(a) - r(b);
  }));
  let drag = $state<string | null>(null);
  let over = $state<string | null>(null);

  function startDrag(e: DragEvent, key: string) {
    drag = key;
    e.dataTransfer?.setData('text/x-quantos-sidebar', key);
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }
  function endDrag() { drag = null; over = null; }
  function overSection(e: DragEvent, key: string) { if (drag?.startsWith('section:')) { e.preventDefault(); over = 'section:' + key; } }
  function dropOn(e: DragEvent, target: string) {
    e.preventDefault();
    const from = drag;
    endDrag();
    if (!from || from === target || from.startsWith('section:') !== target.startsWith('section:')) return;
    const keys = from.startsWith('section:') ? sectionOrder.map((k) => 'section:' + k) : ordered.map((i) => i.key);
    const a = keys.indexOf(from), b = keys.indexOf(target);
    if (a < 0 || b < 0) return;
    keys.splice(a, 1);
    keys.splice(b, 0, from);   // lands where the target was: before it moving up, after it moving down
    if (from.startsWith('section:')) prefs.sidebarSections = keys.map((k) => k.slice(8));
    else prefs.sidebarOrder = keys;
  }
  /** Props that make an item draggable and a drop target. */
  function dnd(key: string) {
    return {
      draggable: true,
      ondragstart: (e: DragEvent) => startDrag(e, key),
      ondragend: endDrag,
      ondragover: (e: DragEvent) => { if (drag && !drag.startsWith('section:')) { e.preventDefault(); over = key; } },
      ondragleave: () => { if (over === key) over = null; },
      ondrop: (e: DragEvent) => dropOn(e, key),
      'data-drop': over === key && drag !== key ? 'before' : undefined
    };
  }

  let collapsed = $state<Record<string, boolean>>({});
  const isActive = (kind: TabKind, arg?: string) => layout.activeTab?.kind === kind && (arg === undefined || layout.activeTab?.arg === arg);

  function open(e: MouseEvent, kind: TabKind, label: string, arg?: string) {
    layout.open({ kind, title: label, arg }, whereFrom(e));
  }
</script>

{#snippet section_pinned()}
    {#if views.pinned.length && showSection('pinned')}
      <section>
        <button class="heading" draggable="true" ondragstart={(e) => startDrag(e, 'section:pinned')} ondragend={endDrag} onclick={() => (collapsed.pinned = !collapsed.pinned)}>
          <Icon name={collapsed.pinned ? 'chevron-right' : 'chevron-down'} size={12} /> Pinned views
        </button>
        {#if !collapsed.pinned}
          {#each views.pinned as v (v.id)}
            <button class="item" class:active={isActive('view', v.id)} onclick={(e) => open(e, 'view', v.name, v.id)}>
              <Icon name={v.icon ?? 'star'} size={15} /> <span>{v.name}</span>
            </button>
          {/each}
        {/if}
      </section>
    {/if}
{/snippet}
{#snippet section_folders()}
    {#if (folders.length || views.saved.some((v) => v.virtualFolder)) && showSection('folders')}
      <section>
        <button class="heading" draggable="true" ondragstart={(e) => startDrag(e, 'section:folders')} ondragend={endDrag} onclick={() => (collapsed.folders = !collapsed.folders)}>
          <Icon name={collapsed.folders ? 'chevron-right' : 'chevron-down'} size={12} /> Folders
        </button>
        {#if !collapsed.folders}
          {#each folders as f (f.path)}
            <button class="item" style="padding-left:{12 + f.depth * 14}px" class:active={isActive('view', 'folder:' + f.path)}
              onclick={(e) => open(e, 'view', f.name, 'folder:' + f.path)}>
              <Icon name="folder" size={15} /> <span>{f.name}</span>
            </button>
          {/each}
          {#each views.saved.filter((v) => v.virtualFolder && !v.pinned) as v (v.id)}
            <button class="item" class:active={isActive('view', v.id)} onclick={(e) => open(e, 'view', v.name, v.id)} title="Virtual folder">
              <Icon name="folder-open" size={15} /> <span>{v.name}</span>
            </button>
          {/each}
        {/if}
      </section>
    {/if}
{/snippet}
{#snippet section_queries()}
    {#if showSection('queries')}
    <section>
      <button class="heading" draggable="true" ondragstart={(e) => startDrag(e, 'section:queries')} ondragend={endDrag} onclick={() => (collapsed.queries = !collapsed.queries)}>
        <Icon name={collapsed.queries ? 'chevron-right' : 'chevron-down'} size={12} /> Saved queries
      </button>
      {#if !collapsed.queries}
        {#each views.saved.filter((v) => !v.pinned && !v.virtualFolder) as v (v.id)}
          <button class="item" class:active={isActive('view', v.id)} onclick={(e) => open(e, 'view', v.name, v.id)}>
            <Icon name={v.virtualFolder ? 'folder' : 'filter'} size={15} /> <span>{v.name}</span>
          </button>
        {/each}
        <button class="item add" onclick={(e) => open(e, 'view', 'New view', 'new')}><Icon name="plus" size={14} /> <span>New view</span></button>
      {/if}
    </section>
    {/if}
{/snippet}
{#snippet section_tags()}
    {#if showSection('tags')}
    <section>
      <button class="heading" draggable="true" ondragstart={(e) => startDrag(e, 'section:tags')} ondragend={endDrag} onclick={() => (collapsed.tags = !collapsed.tags)}>
        <Icon name={collapsed.tags ? 'chevron-right' : 'chevron-down'} size={12} /> Supertags
      </button>
      {#if !collapsed.tags}
        {#each supertags.tree as entry (entry.tag.name)}
          <button class="item" style="padding-left:{12 + entry.depth * 14}px" class:active={isActive('supertags', entry.tag.name)}
            onclick={(e) => open(e, 'supertags', '#' + entry.tag.name, entry.tag.name)}>
            <span class="swatch" style="background:{entry.tag.color ?? 'var(--accent)'}"></span> <span>#{entry.tag.name}</span>
            <span class="count">{entry.count}</span>
          </button>
        {/each}
        <button class="item add" onclick={(e) => open(e, 'supertags', 'Supertags', '')}><Icon name="hash" size={14} /> <span>Manage supertags</span></button>
      {/if}
    </section>
    {/if}
{/snippet}
{#snippet section_tools()}
    {#if showSection('tools')}
    <section>
      <button class="heading" draggable="true" ondragstart={(e) => startDrag(e, 'section:tools')} ondragend={endDrag} onclick={() => (collapsed.tools = !collapsed.tools)}>
        <Icon name={collapsed.tools ? 'chevron-right' : 'chevron-down'} size={12} /> Vault
      </button>
      {#if !collapsed.tools}
        {#each tools as item (item.kind)}
          <button class="item" class:active={isActive(item.kind)} onclick={(e) => open(e, item.kind, item.label)}>
            <Icon name={item.icon} size={15} /> <span>{item.label}</span>
          </button>
        {/each}
      {/if}
    </section>
    {/if}
{/snippet}

<aside class="sidebar" aria-label="Navigation">
  <div class="scroll body">
    {#if customizing}
      <section class="custom" aria-label="Customise the sidebar">
        <p class="hint">Tick what to show; arrows change the order. Saved on this device.</p>
        {#each ordered as item, i (item.key)}
          <div class="crow" {...dnd(item.key)}>
            <label><input type="checkbox" checked={!prefs.sidebarHidden.includes(item.key)} onchange={() => toggle(item.key)} /> <Icon name={item.icon} size={14} /> {item.label}</label>
            <button class="icon-btn" aria-label={'Move ' + item.label + ' up'} disabled={i === 0} onclick={() => move(item.key, -1)}><Icon name="arrow-up" size={12} /></button>
            <button class="icon-btn" aria-label={'Move ' + item.label + ' down'} disabled={i === ordered.length - 1} onclick={() => move(item.key, 1)}><Icon name="arrow-down" size={12} /></button>
          </div>
        {/each}
        <p class="hint">Sections</p>
        {#each sections as sec (sec.key)}
          <div class="crow"><label><input type="checkbox" checked={showSection(sec.key)} onchange={() => toggle('section:' + sec.key)} /> {sec.label}</label></div>
        {/each}
        <div class="cbtns"><button class="btn primary" onclick={() => (customizing = false)}>Done</button><button class="btn ghost" onclick={reset}>Reset</button></div>
      </section>
    {:else}
    <section>
      {#each shownItems as item (item.key)}
        {#if item.kind}
          <button class="item" class:active={isActive(item.kind)} onclick={(e) => open(e, item.kind!, item.label)} oncontextmenu={(e) => { e.preventDefault(); layout.open({ kind: item.kind!, title: item.label }, 'other'); }} {...dnd(item.key)}>
            <Icon name={item.icon} size={15} /> <span>{item.label}</span>
          </button>
        {:else if item.key === 'capture'}
          <button class="item" onclick={() => capture.show()} {...dnd(item.key)}><Icon name="inbox" size={15} /> <span>Quick capture</span></button>
        {:else if isWindowsClient}
          <button class="item" class:active={terminal.open} onclick={() => terminal.toggle()} {...dnd(item.key)}><Icon name="terminal" size={15} /> <span>Terminal</span></button>
        {:else}
          <div class="item disabled" title="The PowerShell terminal runs in the Windows client only." {...dnd(item.key)}><Icon name="terminal" size={15} /> <span>Terminal</span> <span class="requires-windows">Windows client</span></div>
        {/if}
      {/each}
    </section>
    {/if}

    {#each sectionOrder as key (key)}
      <div class="sec" class:drop={over === 'section:' + key && drag !== 'section:' + key} role="presentation"
        ondragover={(e) => overSection(e, key)} ondragleave={() => { if (over === 'section:' + key) over = null; }} ondrop={(e) => dropOn(e, 'section:' + key)}>
        {#if key === 'pinned'}{@render section_pinned()}{:else if key === 'folders'}{@render section_folders()}{:else if key === 'queries'}{@render section_queries()}{:else if key === 'tags'}{@render section_tags()}{:else}{@render section_tools()}{/if}
      </div>
    {/each}
  </div>
  <footer>
    <button class="item" onclick={(e) => open(e, 'settings', 'Settings')} oncontextmenu={(e) => { e.preventDefault(); layout.open({ kind: 'settings', title: 'Settings' }, 'other'); }}><Icon name="settings" size={15} /> <span>Settings</span></button>
    <button class="icon-btn" title="Help (F1)" aria-label="Help" onclick={(e) => open(e, 'help', 'Help')}><Icon name="help" size={15} /></button>
    <button class="icon-btn" title="About Quantos" aria-label="About Quantos" onclick={(e) => open(e, 'about', 'About Quantos')}><Icon name="info" size={15} /></button>
    <button class="icon-btn" title="Customise the sidebar" aria-label="Customise the sidebar" aria-pressed={customizing} onclick={() => (customizing = !customizing)}><Icon name="sliders" size={15} /></button>
  </footer>
</aside>

<style>
  .body { flex: 1; padding: 10px 8px; }
  section { display: grid; gap: 1px; margin-bottom: 14px; }
  .heading { display: flex; align-items: center; gap: 6px; padding: 4px 8px; border: 0; background: none; font: 600 10.5px/1.4 var(--font-mono); letter-spacing: .09em; text-transform: uppercase; color: var(--text-faint); text-align: left; }
  .heading:hover { color: var(--text-muted); }
  .sidebar { grid-area: left; display: flex; flex-direction: column; min-height: 0; background: var(--bg-panel); border: 1px solid var(--line); box-shadow: 0 4px 14px rgb(0 0 0 / 0.16); border-radius: 14px; margin: 0 0 8px 14px; overflow: hidden; }
  .item { display: flex; align-items: center; gap: 9px; width: 100%; padding: calc(6px * var(--d)) 10px calc(6px * var(--d)) 12px; border: 1px solid transparent; border-radius: 7px; background: transparent; color: var(--text-soft); font-size: 14px; text-align: left; margin-bottom: 0; box-shadow: none; }
  .item:hover { background: var(--bg-hover); color: var(--text); border-color: transparent; box-shadow: none; }
  .item.active { background: color-mix(in srgb, var(--accent) 10%, transparent); color: var(--accent-strong); border-color: transparent; box-shadow: none; }
  .item.add { color: var(--text-faint); font-size: 13px; }
  .item.disabled { color: var(--text-faint); cursor: default; }
  .item.disabled:hover { background: none; }
  .swatch { width: 9px; height: 9px; border-radius: 3px; flex: none; }
  .count { font: 11px var(--font-mono); color: var(--text-faint); }
  footer { padding: 8px; border-top: 1px solid var(--line); display: flex; align-items: center; gap: 4px; }
  .custom { gap: 2px; }
  .hint { font-size: 12px; color: var(--text-faint); margin: 2px 8px 6px; }
  .crow { display: flex; align-items: center; gap: 2px; padding: 0 4px 0 8px; border-radius: 6px; }
  .crow:hover { background: var(--bg-hover); }
  .crow label { flex: 1; display: flex; align-items: center; gap: 7px; font-size: 13.5px; color: var(--text-soft); padding: 4px 0; min-width: 0; cursor: pointer; }
  .cbtns { display: flex; gap: 6px; margin: 8px 8px 0; }
  :global(.sidebar [data-drop]) { box-shadow: inset 0 2px 0 var(--accent); }
  .sec.drop { box-shadow: inset 0 2px 0 var(--accent); border-radius: 4px; }
  .heading[draggable='true'] { cursor: grab; }
</style>
