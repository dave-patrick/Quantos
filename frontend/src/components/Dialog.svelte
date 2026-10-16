<script lang="ts">
  import { app } from '../lib/state.svelte';
  let first = $state<HTMLButtonElement | null>(null);
  $effect(() => { if (app.dialog) queueMicrotask(() => first?.focus()); });
  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Escape' && app.dialog) {
      const cancel = app.dialog.actions.find((a) => a.label === 'Cancel') ?? app.dialog.actions[0];
      cancel?.run();
    }
  }
</script>

<svelte:window {onkeydown} />

{#if app.dialog}
  <div class="scrim">
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="dialog-title">
      <h2 id="dialog-title">{app.dialog.title}</h2>
      <p>{app.dialog.message}</p>
      <div class="actions" class:stacked={app.dialog.actions.length > 3}>
        {#each app.dialog.actions as action, i (action.label)}
          {#if i === app.dialog.actions.length - 1}
            <button bind:this={first} class="btn {action.kind ?? ''}" onclick={action.run}>{action.label}</button>
          {:else}
            <button class="btn {action.kind ?? ''}" onclick={action.run}>{action.label}</button>
          {/if}
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
