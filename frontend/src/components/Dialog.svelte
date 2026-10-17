<script lang="ts">
  import { app } from '../lib/state.svelte';
  let box = $state<HTMLDivElement | null>(null);
  let opener: HTMLElement | null = null;
  let shown: unknown = null;
  let open = false;

  // Focus moves into the dialog, starting on Cancel when an action is destructive (so Enter is safe),
  // and goes back to whatever opened it when the last dialog closes. A dialog that replaces another
  // (confirm, then "approve in the Windows client") gets focus too, and keeps the original opener.
  $effect(() => {
    const d = app.dialog;
    if (d && d !== shown) {
      shown = d;
      if (!open) opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      open = true;
      queueMicrotask(() => {
        const buttons = [...(box?.querySelectorAll<HTMLButtonElement>('.actions button') ?? [])];
        const danger = d.actions.some((a) => a.kind === 'danger');
        const cancel = buttons.find((b) => b.textContent?.trim() === 'Cancel');
        (danger && cancel ? cancel : buttons[buttons.length - 1])?.focus();
      });
    } else if (!d && open) {
      open = false;
      shown = null;
      const back = opener;
      opener = null;
      queueMicrotask(() => { if (back?.isConnected && (!document.activeElement || document.activeElement === document.body)) back.focus(); });
    }
  });

  function onkeydown(e: KeyboardEvent) {
    if (!app.dialog) return;
    if (e.key === 'Escape') {
      const cancel = app.dialog.actions.find((a) => a.label === 'Cancel') ?? app.dialog.actions[0];
      cancel?.run();
    } else if (e.key === 'Tab' && box) {
      // Keep keyboard focus inside the dialog.
      const items = [...box.querySelectorAll<HTMLElement>('button, input, select, textarea, [tabindex]:not([tabindex="-1"])')];
      if (!items.length) return;
      const i = items.indexOf(document.activeElement as HTMLElement);
      const next = e.shiftKey ? (i <= 0 ? items.length - 1 : i - 1) : (i === items.length - 1 ? 0 : i + 1);
      e.preventDefault();
      items[next].focus();
    }
  }
</script>

<svelte:window {onkeydown} />

{#if app.dialog}
  <div class="scrim">
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="dialog-title" bind:this={box}>
      <h2 id="dialog-title">{app.dialog.title}</h2>
      <p>{app.dialog.message}</p>
      <div class="actions" class:stacked={app.dialog.actions.length > 3}>
        {#each app.dialog.actions as action (action.label)}
          <button class="btn {action.kind ?? ''}" onclick={action.run}>{action.label}</button>
        {/each}
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; background: rgb(4 8 14 / .6); display: grid; place-items: center; z-index: 80; padding: 16px; }
  .dialog { width: min(460px, 100%); background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 10px; box-shadow: var(--shadow); padding: 20px 22px; }
  h2 { font: 700 18px/1.3 var(--font-display); }
  p { color: var(--text-soft); margin: 10px 0 18px; white-space: pre-wrap; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; flex-wrap: wrap; }
  .actions.stacked { flex-direction: column; align-items: stretch; }
  .actions.stacked .btn { justify-content: flex-start; height: auto; padding: 8px 12px; }
</style>
