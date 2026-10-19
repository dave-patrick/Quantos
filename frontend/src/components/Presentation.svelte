<script lang="ts">
  import { tick } from 'svelte';
  import { presenter } from '../lib/present.svelte';
  import { renderMarkdown } from '../lib/render';
  import { openByTitle } from '../lib/links.svelte';
  import Icon from './Icon.svelte';

  /** Full-screen slides from a note. Arrows, Space or a click move on; Esc ends; N shows speaker notes. */
  let root = $state<HTMLDivElement | null>(null);
  let showNotes = $state(false);
  let idle = $state(false);
  let idleTimer: ReturnType<typeof setTimeout> | undefined;

  const slide = $derived(presenter.slides[presenter.index]);
  // Stored files are served by the backend; links go to their notes once the presentation ends.
  const html = $derived(slide ? renderMarkdown(slide.body).replace(/(<img[^>]*?\ssrc=")(attachments\/)/g, '$1/api/files/$2') : '');
  const notesHtml = $derived(slide?.notes ? renderMarkdown(slide.notes, { links: false }) : '');

  $effect(() => {
    if (!presenter.active) return;
    tick().then(() => {
      root?.focus();
      if (!document.fullscreenElement) root?.requestFullscreen?.().catch(() => {});
    });
  });

  function keys(e: KeyboardEvent) {
    const k = e.key;
    if (['ArrowRight', 'ArrowDown', 'PageDown', ' ', 'Enter'].includes(k)) presenter.go(presenter.index + (e.shiftKey && k === ' ' ? -1 : 1));
    else if (['ArrowLeft', 'ArrowUp', 'PageUp', 'Backspace'].includes(k)) presenter.go(presenter.index - 1);
    else if (k === 'Home') presenter.go(0);
    else if (k === 'End') presenter.go(presenter.slides.length - 1);
    else if (k === 'Escape') presenter.close();
    else if (k.toLowerCase() === 'n') showNotes = !showNotes;
    else if (k.toLowerCase() === 'f') { if (document.fullscreenElement) document.exitFullscreen(); else root?.requestFullscreen?.(); }
    else return;
    e.preventDefault();
    e.stopPropagation();
  }

  function click(e: MouseEvent) {
    const link = (e.target as HTMLElement).closest('a');
    if (link?.classList.contains('wikilink')) {
      e.preventDefault();
      presenter.close();
      openByTitle(link.dataset.target ?? '', 'current');
      return;
    }
    if (link || (e.target as HTMLElement).closest('button, .controls')) return;
    presenter.go(presenter.index + (e.clientX < innerWidth / 4 ? -1 : 1));
  }

  function moved() {
    idle = false;
    clearTimeout(idleTimer);
    idleTimer = setTimeout(() => (idle = true), 2500);
  }
</script>

{#if presenter.active && slide}
  <!-- svelte-ignore a11y_no_noninteractive_tabindex a11y_click_events_have_key_events -->
  <div class="present" class:idle bind:this={root} tabindex="0" role="dialog" aria-modal="true" aria-label={`Presenting ${presenter.title}`}
    onkeydown={keys} onclick={click} onmousemove={moved}>
    <div class="stage">
      {#key presenter.index}
        <article class="slide" aria-live="polite" aria-roledescription="slide" aria-label={`Slide ${presenter.index + 1} of ${presenter.slides.length}`}>
          {@html html}
        </article>
      {/key}
    </div>
    {#if showNotes}
      <aside class="notes" aria-label="Speaker notes">{#if notesHtml}{@html notesHtml}{:else}<span class="faint">No notes for this slide.</span>{/if}</aside>
    {/if}
    <div class="progress" style={`width:${((presenter.index + 1) / presenter.slides.length) * 100}%`}></div>
    <div class="controls">
      <button class="icon-btn" aria-label="Previous slide" disabled={presenter.index === 0} onclick={() => presenter.go(presenter.index - 1)}><Icon name="arrow-left" size={16} /></button>
      <span class="count">{presenter.index + 1} / {presenter.slides.length}</span>
      <button class="icon-btn" aria-label="Next slide" disabled={presenter.index === presenter.slides.length - 1} onclick={() => presenter.go(presenter.index + 1)}><Icon name="arrow-right" size={16} /></button>
      <button class="icon-btn" aria-pressed={showNotes} title="Speaker notes (N)" aria-label="Speaker notes" onclick={() => (showNotes = !showNotes)}><Icon name="file-text" size={16} /></button>
      <button class="icon-btn" title="End (Esc)" aria-label="End presentation" onclick={() => presenter.close()}><Icon name="x" size={16} /></button>
    </div>
  </div>
{/if}

<style>
  .present { position: fixed; inset: 0; z-index: 1000; background: var(--bg); color: var(--text); display: grid; grid-template-rows: 1fr auto; outline: none; cursor: default; }
  .present.idle { cursor: none; }
  .present.idle .controls { opacity: 0; }
  .stage { display: grid; place-items: center; padding: 6vh 8vw; min-height: 0; overflow: auto; }
  .slide { width: min(1100px, 100%); font: 400 clamp(18px, 2.4vw, 34px)/1.45 var(--font-body); animation: in .25s ease-out; }
  @keyframes in { from { opacity: 0; transform: translateY(8px); } }
  .slide :global(h1) { font: 700 clamp(34px, 5.2vw, 76px)/1.1 var(--font-display); letter-spacing: .01em; margin: 0 0 .4em; }
  .slide :global(h2) { font: 700 clamp(26px, 3.6vw, 52px)/1.15 var(--font-display); margin: 0 0 .5em; color: var(--accent-strong); }
  .slide :global(h3) { font: 700 1.1em/1.2 var(--font-display); margin: .6em 0 .3em; }
  .slide :global(p), .slide :global(ul), .slide :global(ol), .slide :global(pre), .slide :global(table), .slide :global(blockquote) { margin: 0 0 .6em; }
  .slide :global(ul), .slide :global(ol) { padding-left: 1.2em; }
  .slide :global(li) { margin: .2em 0; }
  .slide :global(code) { font-family: var(--font-mono); font-size: .85em; background: var(--bg-raised); padding: .05em .3em; border-radius: 4px; }
  .slide :global(pre) { background: var(--bg-raised); border: 1px solid var(--line); border-radius: 8px; padding: .6em .9em; overflow: auto; font-size: .7em; }
  .slide :global(pre code) { background: none; padding: 0; }
  .slide :global(img) { max-width: 100%; max-height: 60vh; border-radius: 6px; }
  .slide :global(a) { color: var(--accent); }
  .slide :global(table) { border-collapse: collapse; font-size: .8em; }
  .slide :global(th), .slide :global(td) { border-bottom: 1px solid var(--line); padding: .3em .7em; text-align: left; }
  .slide :global(.q-diagram svg) { max-height: 62vh; }
  .notes { position: absolute; right: 16px; bottom: 56px; width: min(420px, 40vw); max-height: 40vh; overflow: auto; background: var(--bg-panel); border: 1px solid var(--line-strong); border-radius: 10px; padding: 12px 14px; font-size: 15px; line-height: 1.5; box-shadow: var(--shadow); }
  .progress { position: absolute; left: 0; bottom: 0; height: 3px; background: var(--accent); transition: width .25s; }
  .controls { display: flex; align-items: center; justify-content: center; gap: 6px; padding: 8px 0 12px; transition: opacity .3s; }
  .count { font: 12px var(--font-mono); color: var(--text-faint); min-width: 64px; text-align: center; }
</style>
