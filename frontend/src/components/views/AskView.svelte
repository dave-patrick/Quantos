<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { ai, streamPost, type AiSource } from '../../lib/ai.svelte';
  import { app } from '../../lib/state.svelte';
  import { layout } from '../../lib/layout.svelte';
  import { openByTitle, openNode, whereFrom } from '../../lib/links.svelte';
  import { renderMarkdown } from '../../lib/render';
  import Icon from '../Icon.svelte';

  /** Ask your notes (Release 1.1): answers come from the notes that match, and name them. */
  interface Turn { question: string; answer: string; sources: AiSource[]; error: string; working: boolean }
  let turns = $state<Turn[]>([]);
  let question = $state('');
  let everywhere = $state(false);
  let controller: AbortController | null = null;
  let box = $state<HTMLTextAreaElement | null>(null);
  let scroller = $state<HTMLElement | null>(null);

  onMount(() => { ai.load(); box?.focus(); });
  const working = $derived(turns.some((t) => t.working));

  async function ask() {
    const q = question.trim();
    if (!q || working) return;
    question = '';
    turns.push({ question: q, answer: '', sources: [], error: '', working: true });
    const turn = turns[turns.length - 1];
    controller = new AbortController();
    try {
      await streamPost('ai/ask', { question: q, workspace: everywhere ? null : app.workspace }, (name, data) => {
        if (name === 'sources') turn.sources = data;
        else if (name === 'text') { turn.answer += data; tick().then(() => scroller?.scrollTo({ top: scroller.scrollHeight })); }
      }, controller.signal);
    } catch (e) {
      if ((e as Error).name !== 'AbortError') turn.error = (e as Error).message;
    } finally { turn.working = false; controller = null; }
  }

  function stop() { controller?.abort(); }

  // Links in answers name notes by title ([[Title]]); open them like links in notes.
  function onAnswerClick(e: MouseEvent) {
    const a = (e.target as HTMLElement).closest('a.wikilink') as HTMLAnchorElement | null;
    if (!a?.dataset.target) return;
    e.preventDefault();
    const t = a.dataset.target;
    if (/^[0-9A-HJKMNP-TV-Z]{26}$/.test(t)) openNode(t, whereFrom(e));
    else openByTitle(t, whereFrom(e));
  }

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); ask(); }
  }
</script>

<div class="scroll ask" bind:this={scroller}>
  <div class="inner">
    <header>
      <h1><Icon name="sparkles" size={22} /> Ask your notes</h1>
      {#if ai.chatReady}<span class="faint">{ai.preset(ai.settings.chat?.provider)?.name} · {ai.settings.chat?.model}{ai.searchReady ? ' · finds notes by meaning' : ' · finds notes by words'}</span>{/if}
    </header>

    {#if ai.loaded && !ai.chatReady}
      <div class="empty-state">
        <h3>AI isn't set up</h3>
        <p>Choose a provider in Settings › AI. Ollama or LM Studio keep everything on this computer.</p>
        <button class="btn primary" onclick={() => layout.open({ kind: 'settings', title: 'Settings', arg: 'ai' }, 'current')}>Open AI settings</button>
      </div>
    {:else}
      {#each turns as t, i (i)}
        <section class="turn">
          <p class="q">{t.question}</p>
          {#if t.error}<p class="warn">{t.error}</p>{/if}
          <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
          <div class="answer rendered" onclick={onAnswerClick}>{@html renderMarkdown(t.answer || (t.working ? '…' : ''))}</div>
          {#if t.sources.length}
            <div class="sources"><span class="faint">From</span>
              {#each t.sources as s (s.id)}<button class="chip" onclick={(e) => openNode(s.id, whereFrom(e), s.title)}><Icon name="file-text" size={12} /> {s.title}</button>{/each}
            </div>
          {/if}
        </section>
      {:else}
        <p class="faint hint">Ask about anything in your notes: “What did we decide about the launch date?”, “Summarise my meetings with Sam”. Answers name the notes they come from.</p>
      {/each}

      <div class="composer">
        <textarea bind:this={box} bind:value={question} {onkeydown} rows="2" placeholder="Ask a question about your notes" aria-label="Question"></textarea>
        <div class="tools">
          <label class="check"><input type="checkbox" bind:checked={everywhere} /> All workspaces</label>
          {#if working}<button class="btn" onclick={stop}>Stop</button>{:else}<button class="btn primary" disabled={!question.trim()} onclick={ask}>Ask</button>{/if}
        </div>
      </div>
    {/if}
  </div>
</div>

<style>
  .ask { flex: 1; }
  .inner { max-width: 780px; margin: 0 auto; padding: 24px 28px 40px; }
  header { display: flex; align-items: baseline; gap: 14px; flex-wrap: wrap; margin-bottom: 18px; }
  h1 { display: flex; align-items: center; gap: 10px; font: 700 28px/1.2 var(--font-display); }
  .turn { border-bottom: 1px solid var(--line); padding: 14px 0; }
  .q { font-weight: 600; color: var(--text); margin-bottom: 8px; }
  .answer { color: var(--text-soft); line-height: 1.6; }
  .sources { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; margin-top: 10px; font-size: 13px; }
  .chip { display: inline-flex; gap: 5px; align-items: center; border: 1px solid var(--line-strong); border-radius: 12px; padding: 2px 10px; background: none; color: var(--text-soft); font-size: 12.5px; }
  .chip:hover { border-color: var(--accent); color: var(--accent); }
  .composer { margin-top: 18px; border: 1px solid var(--line-strong); border-radius: 10px; background: var(--bg-input); padding: 8px; }
  textarea { width: 100%; border: 0; background: none; resize: vertical; font: inherit; color: var(--text); padding: 4px; }
  textarea:focus { outline: none; }
  .composer:focus-within { border-color: var(--accent); }
  .tools { display: flex; justify-content: space-between; align-items: center; }
  .check { display: inline-flex; gap: 6px; align-items: center; font-size: 13px; color: var(--text-muted); }
  .hint { font-size: 14px; }
</style>
