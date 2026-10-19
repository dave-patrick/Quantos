<script lang="ts">
  import { aiJob } from '../lib/ai.svelte';
  import { modal } from '../lib/modal';
  import { app } from '../lib/state.svelte';
  import { renderMarkdown } from '../lib/render';
  import Icon from './Icon.svelte';

  /** Shows an AI rewrite as it's written; nothing changes in the note until Replace or Insert. */
  function apply(fn: ((t: string) => void) | null) {
    if (!fn) return;
    fn(aiJob.text.trim());
    aiJob.close();
  }
  async function copy() {
    try { await navigator.clipboard.writeText(aiJob.text.trim()); app.toast('Copied.', 'ok'); } catch { app.toast("Couldn't copy.", 'warn'); }
  }
</script>

{#if aiJob.open}
  <div class="scrim">
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="ai-result-title" use:modal={{ onclose: () => aiJob.close() }}>
      <h2 id="ai-result-title"><Icon name="sparkles" size={18} /> {aiJob.title}</h2>
      {#if aiJob.error}
        <p class="warn">{aiJob.error}</p>
      {:else}
        <div class="result rendered" aria-live="polite" aria-busy={aiJob.working}>{@html renderMarkdown(aiJob.text || '…')}</div>
      {/if}
      <div class="actions">
        {#if aiJob.working}
          <button class="btn" onclick={() => aiJob.stop()}>Stop</button>
        {:else}
          <button class="btn" onclick={() => aiJob.close()}>Discard</button>
          {#if aiJob.text.trim()}
            <button class="btn" onclick={copy}>Copy</button>
            {#if aiJob.insertBelow}<button class="btn" onclick={() => apply(aiJob.insertBelow)}>Insert below</button>{/if}
            {#if aiJob.replace}<button class="btn primary" onclick={() => apply(aiJob.replace)}>Replace selection</button>{/if}
          {/if}
        {/if}
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; background: rgb(4 8 14 / .6); display: grid; place-items: center; z-index: 80; padding: 16px; }
  .dialog { width: min(680px, 100%); max-height: 80vh; display: flex; flex-direction: column; gap: 12px; background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 10px; box-shadow: var(--shadow); padding: 20px 22px; }
  h2 { display: flex; align-items: center; gap: 8px; font: 700 18px/1.3 var(--font-display); }
  .result { overflow: auto; color: var(--text-soft); line-height: 1.6; border: 1px solid var(--line); border-radius: 8px; padding: 10px 14px; background: var(--bg); min-height: 80px; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; flex-wrap: wrap; }
</style>
