<script lang="ts">
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
  import { relativeTime } from '../../lib/dates';
  import type { NodeDto } from '../../lib/types';
  import Editor from '../Editor.svelte';
  import Icon from '../Icon.svelte';
  import Properties from '../Properties.svelte';
  import ConflictView from '../ConflictView.svelte';
  import NodeMenu from '../NodeMenu.svelte';
  import TaskPanel from '../TaskPanel.svelte';
  import { activeEditor } from '../../lib/activeEditor';
  import { recent } from '../../lib/recent.svelte';
  import { openByTitle } from '../../lib/links.svelte';

  let { tab, id, paneIndex }: { tab: Tab; id: string; paneIndex: number } = $props();

  const doc = docs.acquire(id);
  onDestroy(() => docs.release(id));

  let scroller = $state<HTMLDivElement | null>(null);
  let view = $state<EditorView | null>(null);
  let titleDraft = $state('');
  let editingTitle = $state(false);
  let showToc = $state(false);
  let zoom = $state<{ line: number; text: string } | null>(null);
  let restored = false;

  $effect(() => { if (!editingTitle && doc.node) titleDraft = doc.node.title; });

  // Restore the scroll position once content is loaded (workspace switches restore it too).
  $effect(() => {
    if (!restored && doc.node && scroller) {
      restored = true;
      tick().then(() => { if (scroller) scroller.scrollTop = tab.scroll ?? 0; });
    }
  });

  let scrollTimer: ReturnType<typeof setTimeout>;
  function onScroll() {
    clearTimeout(scrollTimer);
    scrollTimer = setTimeout(() => { if (scroller) { tab.scroll = scroller.scrollTop; layout.persist(); } }, 300);
  }

  async function commitTitle() {
    editingTitle = false;
    const next = titleDraft.trim();
    if (!doc.node || !next || next === doc.node.title) { titleDraft = doc.node?.title ?? ''; return; }
    try { await doc.rename(next); } catch (e) { app.error(e); titleDraft = doc.node.title; }
  }

  const crumbs = $derived(doc.node ? doc.node.path.split('/').slice(doc.node.path.startsWith('workspaces/') ? 1 : 0, -1) : []);
  const words = $derived(wordCount(doc.body));
  const toc = $derived(outline(doc.body));
  const target = $derived(prefs.writingTarget[id] ?? 0);

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

  function onKey(e: KeyboardEvent) {
    if (e.key === 'F2') { editingTitle = true; }
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

<div class="node-view">
  <div class="bar">
    <nav class="crumbs" aria-label="Location">
      {#each crumbs as c, i (i)}<span class="crumb">{c}</span><Icon name="chevron-right" size={11} />{/each}
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
      <button class="icon-btn" aria-pressed={showToc} title="Table of contents" onclick={() => (showToc = !showToc)}><Icon name="list" /></button>
      <button class="icon-btn" aria-pressed={doc.raw} title={doc.raw ? 'Back to the editor' : 'Edit the Markdown source'} disabled={doc.node?.readOnly}
        onclick={() => doc.setRaw(!doc.raw)}><Icon name="code" /></button>
      <button class="icon-btn" title="Version history" onclick={() => layout.open({ kind: 'history', nodeId: id, title: `History: ${doc.node?.title ?? ''}` }, 'new')}><Icon name="history" /></button>
      <NodeMenu {doc} {paneIndex} oncopymd={() => copyMarkdown(doc.body).then(() => app.toast('Copied as Markdown.', 'ok'))} oncopyrich={() => copyRich(doc.body).then(() => app.toast('Copied as rich text.', 'ok'))} />
    </div>
  </div>

  <div class="scroll body" bind:this={scroller} onscroll={onScroll}>
    {#if doc.status === 'loading'}
      <div class="empty-state">Loading…</div>
    {:else if doc.status === 'missing'}
      <div class="placeholder">
        <Icon name="alert" size={20} />
        <div><h3>This node no longer exists</h3><p>It may have been deleted outside Quantos. Its earlier versions are still in history.</p>
          <button class="btn" onclick={() => layout.open({ kind: 'history', nodeId: id, title: 'History' }, 'current')}>Open history</button></div>
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
        {#if doc.error && doc.status === 'error'}<div class="notice error"><Icon name="alert" size={15} /> {doc.error}</div>{/if}

        {#if !doc.raw}
          <input class="title" bind:this={titleInput} bind:value={titleDraft} onfocus={() => (editingTitle = true)} onblur={commitTitle}
            onkeydown={(e) => { if (e.key === 'Enter') { e.preventDefault(); (e.target as HTMLInputElement).blur(); view?.focus(); } if (e.key === 'Escape') { titleDraft = doc.node?.title ?? ''; (e.target as HTMLInputElement).blur(); } }}
            aria-label="Title" spellcheck="false" />
          {#if namespace.length}<nav class="ns">{#each namespace as part, i (i)}<button onclick={(e) => openByTitle(namespace.slice(0, i + 1).join('/'), e.shiftKey ? 'other' : 'current')}>{part}</button><span>/</span>{/each}</nav>{/if}
          <div class="meta faint">
            <span class="badge">{doc.node.type}</span>
            {#if doc.node.archived}<span class="badge warn">archived</span>{/if}
            <span>Edited {relativeTime(doc.node.modified)}</span>
          </div>
          {#if doc.node.type === 'task'}<TaskPanel {doc} />{/if}
          <Properties {doc} />
          {#if zoomedBody}
            {#key zoom?.line}
              <Editor text={zoomedBody.text} onchange={editZoomed} onsave={() => doc.save()} bind:view />
            {/key}
          {:else}
            <Editor text={doc.body} version={doc.externalVersion} onchange={(t) => doc.edit(t)} onsave={() => doc.save()} bind:view onfocus={() => { activeEditor.view = view; activeEditor.docId = id; }}
              readOnly={doc.node.readOnly} placeholder="Start writing. Type [[ to link a note, - [ ] for a task." />
          {/if}
        {:else}
          <Editor text={doc.rawText} version={doc.externalVersion} raw onchange={(t) => doc.editRaw(t)} onsave={() => doc.save()} bind:view />
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
  .node-view { flex: 1; display: flex; flex-direction: column; min-height: 0; position: relative; }
  .bar { display: flex; align-items: center; justify-content: space-between; gap: 10px; height: 38px; padding: 0 10px 0 16px; flex: none; }
  .crumbs { display: flex; align-items: center; gap: 4px; min-width: 0; overflow: hidden; color: var(--text-faint); font-size: 12.5px; white-space: nowrap; }
  .crumb { overflow: hidden; text-overflow: ellipsis; }
  .crumb.current { color: var(--text-muted); }
  .crumb.link { border: 0; background: none; color: var(--accent); padding: 0; font-size: inherit; }
  .actions { display: flex; align-items: center; gap: 2px; }
  .save-state { font: 11px var(--font-mono); color: var(--text-faint); margin-right: 6px; min-width: 52px; text-align: right; }
  .save-state.saving, .save-state.dirty { color: var(--text-muted); }
  .save-state.conflict, .save-state.error { color: var(--danger); }
  .body { flex: 1; padding: 6px 28px 0; }
  .page { max-width: calc(var(--editor-width) + 4px); margin: 0 auto; }
  .title { width: 100%; border: 0; background: transparent; color: var(--text); font: 700 32px/1.25 var(--font-display); letter-spacing: .005em; padding: 18px 0 4px; }
  .title:focus { outline: none; box-shadow: none; }
  .ns { display: flex; gap: 4px; font: 12.5px var(--font-mono); color: var(--text-faint); margin-top: 14px; }
  .ns button { border: 0; background: none; color: var(--accent); padding: 0; font: inherit; }
  .meta { display: flex; gap: 10px; align-items: center; font-size: 12.5px; margin-bottom: 10px; }
  .notice { display: flex; gap: 8px; align-items: flex-start; padding: 10px 12px; border-radius: var(--radius); font-size: 14px; margin: 14px 0; }
  .notice.warn { background: var(--warn-wash); color: var(--warn); }
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
