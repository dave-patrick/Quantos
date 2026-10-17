<script lang="ts">
  import { approvals } from '../lib/approvals.svelte';
  import Icon from './Icon.svelte';

  let cancelButton = $state<HTMLButtonElement | null>(null);
  let opener: HTMLElement | null = null;
  let open = false;
  // Focus Cancel while waiting (it's the only thing to do here) and go back afterwards.
  $effect(() => {
    if (approvals.waiting && !open) {
      open = true;
      opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      queueMicrotask(() => cancelButton?.focus());
    } else if (!approvals.waiting && open) {
      open = false;
      const back = opener; opener = null;
      queueMicrotask(() => { if (back?.isConnected && (!document.activeElement || document.activeElement === document.body)) back.focus(); });
    }
  });
  function onkeydown(e: KeyboardEvent) {
    if (!approvals.waiting) return;
    if (e.key === 'Escape') { e.preventDefault(); approvals.cancel(); }
    else if (e.key === 'Tab') { e.preventDefault(); cancelButton?.focus(); }
  }
</script>

<svelte:window {onkeydown} />

{#if approvals.waiting}
  <div class="scrim">
    <div class="card" role="alertdialog" aria-modal="true" aria-labelledby="approval-title">
      <Icon name="shield" size={22} />
      <div>
        <h2 id="approval-title">Approve in the Windows client</h2>
        <p>{approvals.waiting.description}</p>
        <p class="muted">{approvals.inClient ? 'A confirmation window has opened. Approve it there to continue.' : 'For safety, this can only be approved in the Quantos Windows client. Switch to it and approve the request.'}</p>
        <button class="btn" bind:this={cancelButton} onclick={() => approvals.cancel()}>Cancel</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; background: rgb(4 8 14 / .6); display: grid; place-items: center; z-index: 85; padding: 16px; }
  .card { display: flex; gap: 14px; width: min(480px, 100%); background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 12px; padding: 20px; box-shadow: var(--shadow); color: var(--accent); }
  h2 { font: 700 18px/1.3 var(--font-display); color: var(--text); }
  p { color: var(--text-soft); margin: 8px 0; }
  .muted { font-size: 14px; }
</style>
