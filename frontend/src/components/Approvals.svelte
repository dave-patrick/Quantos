<script lang="ts">
  import { approvals } from '../lib/approvals.svelte';
  import Icon from './Icon.svelte';
</script>

{#if approvals.waiting}
  <div class="scrim">
    <div class="card" role="alertdialog" aria-labelledby="approval-title">
      <Icon name="shield" size={22} />
      <div>
        <h2 id="approval-title">Approve in the Windows client</h2>
        <p>{approvals.waiting.description}</p>
        <p class="muted">{approvals.inClient ? 'A confirmation window has opened. Approve it there to continue.' : 'For safety, this can only be approved in the Quantos Windows client. Switch to it and approve the request.'}</p>
        <button class="btn" onclick={() => approvals.cancel()}>Cancel</button>
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
