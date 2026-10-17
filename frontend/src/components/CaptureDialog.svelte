<script lang="ts">
  import { tick } from 'svelte';
  import { capture } from '../lib/capture.svelte';
  import { app } from '../lib/state.svelte';
  import Icon from './Icon.svelte';
  import { modal } from '../lib/modal';

  let area = $state<HTMLTextAreaElement | null>(null);
  let busy = $state(false);
  $effect(() => { if (capture.open) tick().then(() => area?.focus()); });

  async function submit() {
    busy = true;
    try { await capture.submit(); } catch (e) { app.error(e); } finally { busy = false; }
  }

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); submit(); }
  }
</script>

{#if capture.open}
  <div class="scrim">
    <div class="card" role="dialog" aria-modal="true" aria-label="Quick capture" use:modal={{ onclose: () => (capture.open = false) }}>
      <header><Icon name="inbox" size={16} /> <strong>Quick capture</strong></header>
      <textarea bind:this={area} bind:value={capture.text} {onkeydown} rows="5" placeholder="Jot it down. Ctrl+Enter to save."></textarea>
      <div class="row">
        <label>To
          <select class="input" bind:value={capture.target}>
            <option value="daily">Today's daily note</option>
            <option value="inbox">A new note</option>
            <option value="task">A new task</option>
          </select>
        </label>
        <label>Workspace
          <select class="input" bind:value={capture.workspace}>
            {#each app.workspaces as ws (ws)}<option value={ws}>{ws}</option>{/each}
          </select>
        </label>
        <span class="spacer"></span>
        <button class="btn" onclick={() => (capture.open = false)}>Cancel</button>
        <button class="btn primary" disabled={busy || !capture.text.trim()} onclick={submit}>Capture</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; z-index: 76; background: rgb(4 8 14 / .55); display: flex; justify-content: center; align-items: flex-start; padding: 14vh 16px 16px; }
  .card { width: min(600px, 100%); background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 12px; box-shadow: var(--shadow); padding: 14px; display: grid; gap: 10px; }
  header { display: flex; gap: 8px; align-items: center; color: var(--accent); }
  header strong { color: var(--text); font-family: var(--font-display); }
  textarea { width: 100%; resize: vertical; border-radius: 8px; border: 1px solid var(--line-strong); background: var(--bg-input); padding: 10px 12px; font-size: 15px; line-height: 1.5; }
  textarea:focus { outline: none; border-color: var(--accent); }
  .row { display: flex; gap: 10px; align-items: end; flex-wrap: wrap; }
  label { display: grid; gap: 4px; font-size: 12px; color: var(--text-faint); }
  label .input { height: 30px; width: auto; }
  .spacer { flex: 1; }
</style>
