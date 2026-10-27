<script lang="ts">
  import { ago } from '../../lib/today.svelte';
  import { onDestroy, onMount, tick } from 'svelte';
  import type { EditorView } from '@codemirror/view';
  import { EditorView as CMView } from '@codemirror/view';
  import { api } from '../../lib/api';
  import { docs } from '../../lib/docs.svelte';
  import { layout, type Tab } from '../../lib/layout.svelte';
  import { app } from '../../lib/state.svelte';
  import { prefs } from '../../lib/prefs.svelte';
  import { copyMarkdown, copyRich, outline } from '../../lib/render';
  import { wordCount, readingMinutes } from '../../lib/text';
  import type { NodeDto } from '../../lib/types';
  import Editor from '../Editor.svelte';
  import Icon from '../Icon.svelte';
  import Properties from '../Properties.svelte';
  import ConflictView from '../ConflictView.svelte';
  import NodeMenu from '../NodeMenu.svelte';
  import TaskPanel from '../TaskPanel.svelte';
  import Whiteboard from '../Whiteboard.svelte';
  import { recent } from '../../lib/recent.svelte';
  import { openByTitle, whereFrom } from '../../lib/links.svelte';
  import { writeGate, writeKey } from '../../lib/writeGate';

  let { tab, id, paneIndex, popup = false }: { tab: Tab; id: string; paneIndex: number; popup?: boolean } = $props();

  const doc = docs.acquire(id);
  onDestroy(() => docs.release(id));

  let scroller = $state<HTMLDivElement | null>(null);
  let view = $state<EditorView | null>(null);
  let titleDraft = $state('');
  /** Locked note or reading mode: the text, title and properties are shown but can't be changed. */
  const noEdit = $derived(!!doc.node?.locked || prefs.reading);
  let editingTitle = $state(false);
  let titleFailed = $state(false);
  let titleSaving = $state(false);
  let showToc = $state(false);
  let zoom = $state<{ line: number; text: string } | null>(null);
  let restored = false;
  let restoreObserver: ResizeObserver | null = null;
  let restoreTimer: ReturnType<typeof setTimeout>;
  onDestroy(() => { restoreObserver?.disconnect(); clearTimeout(restoreTimer); clearTimeout(scrollTimer); });

  $effect(() => { if (!editingTitle && !titleFailed && !titleSaving && doc.node) titleDraft = doc.node.title; });

  // Restore the scroll position once content is loaded (workspace switches restore it too).
  $effect(() => {
    if (!restored && doc.node && scroller && (view || doc.node.type === 'canvas')) {
      restored = true;
      const position = tab.scroll ?? 0;
      // Virtualized line heights settle after the editor mounts. Wait for the content's
      // measured height so a long note is not clamped to the initial height estimate.
      tick().then(() => {
        if (!scroller) return;
        const finish = () => {
          restoreObserver?.disconnect(); restoreObserver = null;
          clearTimeout(restoreTimer);
          if (scroller) { scroller.scrollTop = position; tab.scroll = scroller.scrollTop; }
        };
        if (!view || position === 0) { finish(); return; }
        restoreObserver = new ResizeObserver(() => {
          if (!scroller) return;
          scroller.scrollTop = position;
          if (scroller.scrollHeight - scroller.clientHeight >= position) requestAnimationFrame(finish);
        });
        restoreObserver.observe(view.contentDOM);
        restoreTimer = setTimeout(finish, 1500);
      });
    }
  });

  let scrollTimer: ReturnType<typeof setTimeout>;
  function onScroll() {
    if (restoreObserver) return;
    if (scroller) tab.scroll = scroller.scrollTop;
    clearTimeout(scrollTimer);
    scrollTimer = setTimeout(() => { if (scroller) { tab.scroll = scroller.scrollTop; layout.persist(); } }, 300);
  }

  async function commitTitle() {
    editingTitle = false;
    if (titleSaving) return;
    const next = titleDraft.trim();
    if (!doc.node || !next || next === doc.node.title) { titleDraft = doc.node?.title ?? ''; return; }
    titleSaving = true;
    await docs.track(doc.rename(next).then(() => { titleFailed = false; }, (e) => { titleFailed = true; app.error(e); }).finally(() => { titleSaving = false; }));
  }

  /**
   * Where the note lives, each step clickable: the workspace and its Notes folder open the notes browser,
   * Daily the daily stream, Tasks the task list, and any other folder a view of that folder.
   */
  const crumbs = $derived.by(() => {
    if (!doc.node) return [];
    const parts = doc.node.path.split('/');
    const inWorkspace = parts[0] === 'workspaces';
    const inLinked = parts[0] === 'linked';
    const scoped = inWorkspace || inLinked;
    const out: { label: string; go: (e: MouseEvent) => void }[] = [];
    const notes = (e: MouseEvent) => layout.open({ kind: 'notes', title: 'Notes' }, whereFrom(e));
    parts.slice(0, -1).forEach((part, i) => {
      if (scoped && i === 0) return;
      const folder = parts.slice(0, i + 1).join('/');
      const top = scoped ? parts[2] : parts[0];
      if (scoped && i === 1) out.push({ label: part, go: notes });
      else if (inWorkspace && i === 2 && part === 'nodes') out.push({ label: 'Notes', go: notes });
      else if (inWorkspace && top === 'daily') out.push({ label: i === 2 ? 'Daily' : part, go: (e) => layout.open({ kind: 'daily', title: 'Daily' }, whereFrom(e)) });
      else if (inWorkspace && i === 2 && part === 'tasks') out.push({ label: 'Tasks', go: (e) => layout.open({ kind: 'tasks', title: 'Tasks' }, whereFrom(e)) });
      else if (part === 'supertags') out.push({ label: 'Supertags', go: (e) => layout.open({ kind: 'supertags', title: 'Supertags', arg: '' }, whereFrom(e)) });
      else if (part === 'templates' && i === 0) out.push({ label: 'Templates', go: (e) => layout.open({ kind: 'templates', title: 'Templates' }, whereFrom(e)) });
      else out.push({ label: part === 'archive' ? 'Archive' : part, go: (e) => layout.open({ kind: 'view', title: part, arg: 'folder:' + folder }, whereFrom(e)) });
    });
    return out;
  });
  const words = $derived(wordCount(doc.body));
  const toc = $derived(outline(doc.body));
  // Stored on the note (writing_target), so it follows the file; older targets kept in this browser still show.
  const target = $derived(Number(doc.node?.properties?.writing_target) || prefs.writingTarget[id] || 0);

  function jumpTo(line: number) {
    if (!view) return;
    const pos = view.state.doc.line(Math.min(line, view.state.doc.lines)).from;
    view.dispatch({ selection: { anchor: pos }, effects: CMView.scrollIntoView(pos, { y: 'start', yMargin: 60 }) });
    view.focus();
  }

  async function restoreTrashed() {
    if (!doc.trashed?.entryId) return;
    try { await api.post(`trash/${doc.trashed.entryId}/restore`); } catch (e) { app.error(e); }
  }

  async function trashDoc() {
    if (!doc.node) return;
    const ok = await app.confirm(`Move “${doc.node.title}” to the trash?`, 'You can restore it from the trash.', 'Move to trash', 'danger');
    if (!ok) return;
    await doc.flush();
    await api.post(`nodes/${doc.id}/trash`);
    app.toast('Moved to trash.', 'ok', { label: 'Undo', run: async () => {
      const entries = await api.get<any[]>('trash');
      const entry = entries.find((e) => e.id === doc.id);
      if (entry) await api.post(`trash/${entry.entryId}/restore`);
    } });
    layout.open({ kind: 'notes', title: 'Notes' }, 'current');
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'F2' && (e.target as HTMLElement)?.closest('.node-view') === scroller?.parentElement) { editingTitle = true; }
  }

  onMount(() => {
    if (tab.title === 'Note' && doc.node) layout.retitle(id, doc.node.title);
    recent.touch(id);
  });

  // Namespaced titles (Projects/Apollo/Budget) get clickable parents.
  const namespace = $derived(doc.node?.title.includes('/') ? doc.node.title.split('/').slice(0, -1) : []);

  // A fresh note gets the cursor: the title if it is still "Untitled", otherwise the body.
  let focused = false;
  let titleInput = $state<HTMLInputElement | null>(null);
  $effect(() => {
    if (focused || !doc.node || doc.node.body.trim() !== '') return;
    if (doc.node.title === 'Untitled' && titleInput) { focused = true; const el = titleInput; setTimeout(() => { el.focus(); el.select(); }, 0); }
    else if (view) { focused = true; view.focus(); }
  });

  /** Outline zoom: show only one heading's section; breadcrumbs lead back out. */
  const zoomedBody = $derived.by(() => {
    if (!zoom) return null;
    const lines = doc.body.split('\n');
    const start = zoom.line - 1;
    const level = /^(#+)/.exec(lines[start] ?? '')?.[1].length ?? 1;
    let end = lines.length;
    for (let i = start + 1; i < lines.length; i++) {
      const m = /^(#+)\s/.exec(lines[i]);
      if (m && m[1].length <= level) { end = i; break; }
    }
    return { start, end, text: lines.slice(start, end).join('\n') };
  });

  function editZoomed(text: string) {
    if (!zoomedBody) return;
    const lines = doc.body.split('\n');
    lines.splice(zoomedBody.start, zoomedBody.end - zoomedBody.start, ...text.split('\n'));
    doc.edit(lines.join('\n'));
  }

  function onNodeUpdated(node: NodeDto) { void node; }
</script>

<svelte:window onkeydown={onKey} />

<div class="node-view" data-node-id={id}>
  <div class="bar">
    <nav class="crumbs" aria-label="Location">
      {#if !popup}{#each crumbs as c, i (i)}<button class="crumb link" title={`Go to ${c.label}`} onclick={c.go}>{c.label}</button><Icon name="chevron-right" size={11} />{/each}{/if}
      {#if zoom}
        <button class="crumb link" onclick={() => (zoom = null)}>{doc.node?.title}</button><Icon name="chevron-right" size={11} />
        <span class="crumb current">{zoom.text}</span>
      {:else}
        <span class="crumb current">{doc.node?.title ?? ''}</span>
      {/if}
    </nav>
    <div class="actions">
      <span class="save-state {doc.status}">
        {#if doc.status === 'saving'}Saving…{:else if doc.status === 'dirty'}Edited{:else if doc.status === 'saved'}Saved{:else if doc.status === 'conflict'}Conflict{:else if doc.status === 'error'}Not saved{:else if doc.status === 'readonly'}Read-only{/if}
      </span>
      {#if !popup}
      <button class="btn ghost phone-details" onclick={() => { app.rightTab = 'inspector'; app.rightOpen = true; }}>Details</button>
      <button class="icon-btn" aria-pressed={showToc} title="Table of contents" onclick={() => (showToc = !showToc)}><Icon name="list" /></button>
      <button class="icon-btn" aria-pressed={doc.raw} title={doc.raw ? 'Back to the editor' : 'Edit the Markdown source'} disabled={doc.node?.readOnly || noEdit}
        onclick={() => doc.setRaw(!doc.raw)}><Icon name="code" /></button>
      <button class="icon-btn" title="Version history" onclick={() => layout.open({ kind: 'history', nodeId: id, title: `History: ${doc.node?.title ?? ''}` }, 'new')}><Icon name="history" /></button>
      <button class="icon-btn icon-btn-danger" title="Move to trash" onclick={trashDoc}><Icon name="trash" size={14} /></button>
      <NodeMenu {doc} {paneIndex} onoutline={() => (showToc = !showToc)} oncopymd={() => copyMarkdown(doc.body).then(() => app.toast('Copied as Markdown.', 'ok'))} oncopyrich={() => copyRich(doc.body).then(() => app.toast('Copied as rich text.', 'ok'))} />
      {/if}
    </div>
  </div>

  <div class="scroll body" bind:this={scroller} onscroll={onScroll}>
    {#if doc.status === 'loading'}
      <div class="empty-state">Loading…</div>
    {:else if doc.status === 'missing'}
      <div class="placeholder">
        <Icon name="alert" size={20} />
        <div><h3>This node no longer exists</h3><p>It may have been deleted outside Quantos. Its earlier versions are still in history.</p>
          {#if !popup}<button class="btn" onclick={() => layout.open({ kind: 'history', nodeId: id, title: 'History' }, 'current')}>Open history</button>{/if}</div>
      </div>
    {:else if doc.trashed}
      <div class="placeholder">
        <Icon name="trash" size={20} />
        <div><h3>“{doc.trashed.title}” is in the trash</h3><p>Restoring reconnects it everywhere it was referenced.</p>
          <button class="btn primary" onclick={restoreTrashed}>Restore</button></div>
      </div>
    {:else if doc.conflict}
      <ConflictView {doc} />
    {:else if doc.node}
      <article class="page">
        {#if doc.node.readOnly}
          <div class="notice warn"><Icon name="lock" size={15} />
            {doc.node.yamlError ? `The frontmatter isn't valid YAML (${doc.node.yamlError}). Fix it in the source view; the visual editor is off so nothing is lost.` : `This file uses schema ${doc.node.schema}, newer than this version of Quantos understands. It's read-only.`}
          </div>
        {/if}
        {#if doc.node.locked && !doc.node.readOnly}
          <div class="notice lock"><Icon name="lock" size={15} /> Locked: nobody can edit this note, including agents and automations. <button class="btn ghost" onclick={() => doc.setProperties({}, ['locked'])}>Unlock</button></div>
        {:else if prefs.reading && !doc.node.readOnly}
          <div class="notice lock"><Icon name="book" size={15} /> Reading mode is on. <button class="btn ghost" onclick={() => (prefs.reading = false)}>Edit</button></div>
        {/if}
        {#if doc.error && doc.status === 'error'}<div class="notice error"><Icon name="alert" size={15} /> {doc.error}</div>{/if}

        {#if !doc.raw}
          <!-- The editable title is the page's heading, so screen readers can jump to it. -->
          <h1 class="title-heading" aria-label={titleDraft || 'Untitled'}><input class="title" bind:this={titleInput} bind:value={titleDraft} onfocus={() => (editingTitle = true)} onblur={commitTitle} readonly={noEdit || titleSaving}
            onkeydown={(e) => { if (e.key === 'Enter') { e.preventDefault(); (e.target as HTMLInputElement).blur(); view?.focus(); } if (e.key === 'Escape') { titleDraft = doc.node?.title ?? ''; titleFailed = false; writeGate.dismiss(writeKey('POST', `nodes/${id}/rename`, { title: '' })); (e.target as HTMLInputElement).blur(); } }}
            aria-label="Title" spellcheck="false" /></h1>
          {#if namespace.length && !popup}<nav class="ns">{#each namespace as part, i (i)}<button onclick={(e) => openByTitle(namespace.slice(0, i + 1).join('/'), e.shiftKey ? 'other' : 'current')}>{part}</button><span>/</span>{/each}</nav>{/if}
          <div class="meta faint">
            <span class="badge">{doc.node.type}</span>
            {#if doc.node.archived}<span class="badge warn">archived</span>{/if}
            <span>Edited {ago(doc.node.modified)}</span>
          </div>
          {#if doc.node.type !== 'canvas'}
            <fieldset class="plain" disabled={noEdit}>
              <div class="node-properties" class:popup-properties={popup}><Properties {doc} editSchema={!popup} /></div>
              {#if doc.node.type === 'task'}<TaskPanel {doc} />{/if}
            </fieldset>
          {/if}
          {#if doc.node.type === 'canvas'}
            <Whiteboard {doc} />
          {:else if zoomedBody}
            {#key zoom?.line}
              <Editor text={zoomedBody.text} readOnly={noEdit} onchange={editZoomed} onsave={() => doc.save()} bind:view />
            {/key}
          {:else}
            <Editor text={doc.body} version={doc.externalVersion} onchange={(t) => doc.edit(t)} onsave={() => doc.save()} bind:view docId={id} path={doc.node.path}
              readOnly={doc.node.readOnly || noEdit} placeholder="Start writing. Type [[ to link a note, - [ ] for a task." />
          {/if}
        {:else}
          <Editor text={doc.rawText} version={doc.externalVersion} raw readOnly={noEdit && !doc.node?.readOnly} onchange={(t) => doc.editRaw(t)} onsave={() => doc.save()} bind:view />
        {/if}
      </article>
    {/if}
  </div>

  {#if showToc && toc.length}
    <aside class="toc" aria-label="Table of contents">
      <div class="label">Contents</div>
      {#each toc as h (h.line)}
        <div class="toc-row" style="padding-left:{(h.level - 1) * 12}px">
          <button class="toc-item" onclick={() => jumpTo(h.line)}>{h.text}</button>
          <button class="zoom" title="Zoom into this section" onclick={() => (zoom = { line: h.line, text: h.text })}><Icon name="target" size={12} /></button>
        </div>
      {/each}
    </aside>
  {/if}

  {#if prefs.showWordCount && doc.node && !doc.raw}
    <footer class="counts">
      <span>{words.toLocaleString()} words</span>
      <span>{doc.body.length.toLocaleString()} characters</span>
      {#if words}<span>{readingMinutes(words)} min read</span>{/if}
      {#if target > 0}
        <span class="target" title="Writing target">
          <span class="meter"><span style="width:{Math.min(100, (words / target) * 100)}%"></span></span>
          {words}/{target}
        </span>
      {/if}
    </footer>
  {/if}
</div>

<style>
  .popup-properties { display: block !important; }
  .node-view { flex: 1; display: flex; flex-direction: column; min-height: 0; position: relative; }
  .bar { display: flex; align-items: center; justify-content: space-between; gap: 10px; height: 38px; padding: 0 10px 0 16px; flex: none; }
  .crumbs { display: flex; align-items: center; gap: 4px; min-width: 0; overflow: hidden; color: var(--text-faint); font-size: 12.5px; white-space: nowrap; }
  .crumb { overflow: hidden; text-overflow: ellipsis; }
  .crumb.current { color: var(--text-muted); }
  .crumb.link { border: 0; background: none; color: var(--text-faint); padding: 0; font-size: inherit; cursor: pointer; }
  .crumb.link:hover, .crumb.link:focus-visible { color: var(--accent); text-decoration: underline; }
  .actions { display: flex; align-items: center; gap: 2px; }
  .phone-details { display: none; }
  .icon-btn-danger { color: var(--danger); }
  .icon-btn-danger:hover { color: var(--danger); background: var(--danger-wash); }
  .save-state { font: 11px var(--font-mono); color: var(--text-faint); margin-right: 6px; min-width: 52px; text-align: right; }
  .save-state.saving, .save-state.dirty { color: var(--text-muted); }
  .save-state.conflict, .save-state.error { color: var(--danger); }
  .body { flex: 1; padding: 6px 28px 0; }
  .page { max-width: calc(var(--editor-width) + 4px); margin: 0 auto; }
  .title-heading { margin: 0; font: inherit; }
  .title { width: 100%; border: 0; background: transparent; color: var(--text); font: 700 32px/1.25 var(--font-display); letter-spacing: .005em; padding: 18px 0 4px; }
  .title:focus { outline: none; box-shadow: none; }
  .ns { display: flex; gap: 4px; font: 12.5px var(--font-mono); color: var(--text-faint); margin-top: 14px; }
  .ns button { border: 0; background: none; color: var(--accent); padding: 0; font: inherit; }
  .meta { display: flex; gap: 10px; align-items: center; font-size: 12.5px; margin-bottom: 10px; }
  .notice { display: flex; gap: 8px; align-items: flex-start; padding: 10px 12px; border-radius: var(--radius); font-size: 14px; margin: 14px 0; }
  .notice.warn { background: var(--warn-wash); color: var(--warn); }
  .notice.lock { background: var(--accent-wash); color: var(--text-soft); align-items: center; }
  .notice.lock .btn { margin-left: auto; }
  fieldset.plain { border: 0; padding: 0; margin: 0; min-width: 0; }
  .notice.error { background: var(--danger-wash); color: var(--danger); }
  .placeholder { display: flex; gap: 14px; max-width: 560px; margin: 60px auto; padding: 18px 20px; border: 1px dashed var(--line-strong); border-radius: 10px; color: var(--text-muted); }
  .placeholder h3 { font: 700 16px/1.3 var(--font-display); color: var(--text); }
  .placeholder p { margin: 6px 0 12px; }
  .toc { position: absolute; right: 16px; top: 46px; width: 230px; max-height: 60%; overflow: auto; padding: 10px; border-radius: 10px; background: var(--bg-raised); border: 1px solid var(--line-strong); box-shadow: var(--shadow); z-index: 5; }
  .toc .label { margin-bottom: 6px; }
  .toc-row { display: flex; align-items: center; }
  .toc-item { flex: 1; text-align: left; border: 0; background: none; color: var(--text-soft); padding: 4px 6px; border-radius: 5px; font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .toc-item:hover { background: var(--bg-hover); color: var(--text); }
  .zoom { border: 0; background: none; color: var(--text-faint); padding: 3px; border-radius: 4px; opacity: 0; }
  .toc-row:hover .zoom { opacity: 1; }
  .counts { display: flex; gap: 16px; justify-content: flex-end; padding: 4px 16px; font: 11px var(--font-mono); color: var(--text-faint); border-top: 1px solid var(--line); flex: none; }
  .target { display: inline-flex; gap: 6px; align-items: center; }
  .meter { width: 70px; height: 4px; background: var(--line); border-radius: 4px; overflow: hidden; display: inline-block; }
  .meter span { display: block; height: 100%; background: var(--ok); }
</style>
