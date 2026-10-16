<script lang="ts">
  import { onDestroy, onMount } from 'svelte';
  import type { EditorView } from '@codemirror/view';
  import { createEditor, syncText, typewriter, editable } from '../lib/editor/setup';
  import { typewriterScrolling } from '../lib/editor/writing';
  import { EditorState } from '@codemirror/state';
  import { EditorView as View } from '@codemirror/view';
  import { linkHooks, taskHooks, tasksVersion } from '../lib/links.svelte';
  import { prefs } from '../lib/prefs.svelte';

  let {
    text,
    version = 0,
    onchange,
    onsave,
    onfocus,
    readOnly = false,
    raw = false,
    placeholder = '',
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
      hooks: { ...linkHooks, ...taskHooks, onChange: (t) => onchange(t), onSave: () => onsave?.(), onFocus: () => onfocus?.() }
    });
    lastVersion = version;
  });

  onDestroy(() => view?.destroy());

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

  // Re-render task chips when task data changes.
  $effect(() => {
    void tasksVersion.value;
    view?.dispatch({ effects: [] });
  });
</script>

<div class="editor" class:raw bind:this={host}></div>

<style>
  .editor { min-height: 100%; }
  .editor.raw :global(.cm-scroller) { font-family: var(--font-mono); font-size: 13.5px; }
  .editor.raw :global(.cm-content) { max-width: 980px; }
</style>
