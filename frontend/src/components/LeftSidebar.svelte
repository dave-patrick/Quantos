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
    { kind: 'daily', label: 'Daily stream', icon: 'calendar' },
    { kind: 'myday', label: 'My Day', icon: 'sun' },
    { kind: 'tasks', label: 'Tasks', icon: 'check-square' },
    { kind: 'timeline', label: 'Timeline', icon: 'clock' },
    { kind: 'review', label: 'Stale tasks', icon: 'history' },
    { kind: 'search', label: 'Search', icon: 'search' },
    { kind: 'graph', label: 'Graph', icon: 'graph' }
  ];

  const tools: { kind: TabKind; label: string; icon: string }[] = [
    { kind: 'templates', label: 'Templates', icon: 'copy' },
    { kind: 'dictionary', label: 'Metadata dictionary', icon: 'book' },
    { kind: 'diagnostics', label: 'Vault diagnostics', icon: 'activity' },
    { kind: 'attachments', label: 'Attachment auditor', icon: 'paperclip' },
    { kind: 'trash', label: 'Trash', icon: 'trash' }
  ];

  let collapsed = $state<Record<string, boolean>>({});
  const isActive = (kind: TabKind, arg?: string) => layout.activeTab?.kind === kind && (arg === undefined || layout.activeTab?.arg === arg);

  function open(e: MouseEvent, kind: TabKind, label: string, arg?: string) {
    layout.open({ kind, title: label, arg }, whereFrom(e));
  }
</script>

<aside class="sidebar" aria-label="Navigation">
  <div class="scroll body">
    <section>
      {#each nav as item (item.kind)}
        <button class="item" class:active={isActive(item.kind)} onclick={(e) => open(e, item.kind, item.label)}>
          <Icon name={item.icon} size={15} /> <span>{item.label}</span>
        </button>
      {/each}
      <button class="item" onclick={() => capture.show()}><Icon name="inbox" size={15} /> <span>Quick capture</span></button>
      {#if isWindowsClient}
        <button class="item" class:active={terminal.open} onclick={() => terminal.toggle()}><Icon name="terminal" size={15} /> <span>Terminal</span></button>
      {:else}
        <div class="item disabled" title="The PowerShell terminal runs in the Windows client only."><Icon name="terminal" size={15} /> <span>Terminal</span> <span class="requires-windows">Windows client</span></div>
      {/if}
    </section>

    {#if views.pinned.length}
      <section>
        <button class="heading" onclick={() => (collapsed.pinned = !collapsed.pinned)}>
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

    {#if folders.length || views.saved.some((v) => v.virtualFolder)}
      <section>
        <button class="heading" onclick={() => (collapsed.folders = !collapsed.folders)}>
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

    <section>
      <button class="heading" onclick={() => (collapsed.queries = !collapsed.queries)}>
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

    <section>
      <button class="heading" onclick={() => (collapsed.tags = !collapsed.tags)}>
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

    <section>
      <button class="heading" onclick={() => (collapsed.tools = !collapsed.tools)}>
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
  </div>
  <footer>
    <button class="item" onclick={(e) => open(e, 'settings', 'Settings')}><Icon name="settings" size={15} /> <span>Settings</span></button>
  </footer>
</aside>

<style>
  .sidebar { grid-area: left; display: flex; flex-direction: column; min-height: 0; background: var(--bg-raised); border-right: 1px solid var(--line); }
  .body { flex: 1; padding: 10px 8px; }
  section { display: grid; gap: 1px; margin-bottom: 14px; }
  .heading { display: flex; align-items: center; gap: 6px; padding: 4px 8px; border: 0; background: none; font: 600 10.5px/1.4 var(--font-mono); letter-spacing: .09em; text-transform: uppercase; color: var(--text-faint); text-align: left; }
  .heading:hover { color: var(--text-muted); }
  .item { display: flex; align-items: center; gap: 9px; width: 100%; padding: calc(6px * var(--d)) 10px calc(6px * var(--d)) 12px; border: 0; border-radius: 6px; background: transparent; color: var(--text-soft); font-size: 14px; text-align: left; }
  .item span:not(.swatch):not(.count):not(.requires-windows) { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .item:hover { background: var(--bg-hover); color: var(--text); }
  .item.active { background: var(--bg-active); color: var(--accent-strong); }
  .item.add { color: var(--text-faint); font-size: 13px; }
  .item.disabled { color: var(--text-faint); cursor: default; }
  .item.disabled:hover { background: none; }
  .swatch { width: 9px; height: 9px; border-radius: 3px; flex: none; }
  .count { font: 11px var(--font-mono); color: var(--text-faint); }
  footer { padding: 8px; border-top: 1px solid var(--line); }
</style>
