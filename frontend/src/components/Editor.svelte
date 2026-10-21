<script lang="ts">
  import { onDestroy, onMount } from 'svelte';
  import type { EditorView } from '@codemirror/view';
  import { createEditor, syncText, typewriter, editable, collapsibleHeadings } from '../lib/editor/setup';
  import { headingArrows, headingFolding, headingsChanged } from '../lib/editor/headings';
  import { refreshTasks } from '../lib/editor/tasks';
  import { typewriterScrolling } from '../lib/editor/writing';
  import { EditorState } from '@codemirror/state';
  import { EditorView as View } from '@codemirror/view';
  import { linkHooks, taskHooks, tasksVersion } from '../lib/links.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { activeEditor, editorDocs } from '../lib/activeEditor';
  import { toggleSpellCheck } from '../lib/editor/spellcheck';

  let {
    text,
    version = 0,
    onchange,
    onsave,
    onfocus,
    readOnly = false,
    raw = false,
    placeholder = '',
    docId = null,
    path = '',
    view = $bindable<EditorView | null>(null)
  }: {
    text: string;
    version?: number;
    onchange: (text: string) => void;
    onsave?: () => void;
    onfocus?: () => void;
    readOnly?: boolean;
    raw?: boolean;
    placeholder?: string;
    /** The note this editor shows, for editor commands (extract, embed…). */
    docId?: string | null;
    /** Where the note lives, so a relative link points at the file next to it. */
    path?: string;
    view?: EditorView | null;
  } = $props();

  let host: HTMLDivElement;
  let lastVersion = -1;

  onMount(() => {
    view = createEditor(host, {
      text,
      raw,
      readOnly,
      placeholder,
      hooks: { ...linkHooks, ...taskHooks, notePath: () => path || null, onChange: (t) => onchange(t), onSave: () => onsave?.(), onFocus: () => { activeEditor.view = view; activeEditor.docId = docId; onfocus?.(); } }
    });
    lastVersion = version;
    if (view) editorDocs.set(view, () => docId);
  });

  onDestroy(() => {
    if (activeEditor.view === view) { activeEditor.lastPos = view?.state.selection.main.head ?? null; activeEditor.view = null; }
    view?.destroy();
  });

  // Outside changes (merges, server adjustments, reloads) are applied as minimal edits.
  $effect(() => {
    const v = version;
    const t = text;
    if (view && v !== lastVersion) {
      lastVersion = v;
      syncText(view, t);
    }
  });

  $effect(() => {
    const ro = readOnly;
    view?.dispatch({ effects: editable.reconfigure([EditorState.readOnly.of(ro), View.editable.of(!ro)]) });
  });

  $effect(() => {
    const on = prefs.typewriter;
    view?.dispatch({ effects: typewriter.reconfigure(on ? typewriterScrolling : []) });
  });

  $effect(() => {
    const on = prefs.collapsibleHeadings;
    view?.dispatch({ effects: [collapsibleHeadings.reconfigure(on ? [headingFolding, headingArrows] : []), headingsChanged.of(null)] });
  });

  $effect(() => {
    const on = prefs.spellCheck;
    view?.dispatch({ effects: toggleSpellCheck.of(on) });
  });

  // Re-render task chips when task data changes.
  $effect(() => {
    void tasksVersion.value;
    view?.dispatch({ effects: refreshTasks.of(null) });
  });
</script>

<div class="editor" class:raw bind:this={host}></div>

<style>
  .editor { min-height: 100%; }
  .editor.raw :global(.cm-scroller) { font-family: var(--font-mono); font-size: 13.5px; }
  .editor.raw :global(.cm-content) { max-width: 980px; }
  /* Source view shows the file as it is: no heading sizes (a line above --- would otherwise read as a heading). */
  .editor.raw :global(.cm-line), .editor.raw :global(.cm-line *) { font-size: inherit !important; font-family: var(--font-mono) !important; font-weight: 400 !important; line-height: 1.6; }
</style>
