<script lang="ts">
  import { app } from '../lib/state.svelte';
  import Icon from './Icon.svelte';
  const icons = { info: 'info', ok: 'check', warn: 'alert', error: 'alert' } as const;
</script>

<div class="toasts" role="status" aria-live="polite">
  {#each app.toasts as t (t.id)}
    <div class="toast {t.kind}">
      <Icon name={icons[t.kind]} size={16} />
      <span>{t.message}</span>
      {#if t.action}<button class="btn ghost" onclick={() => { t.action!.run(); app.dismiss(t.id); }}>{t.action.label}</button>{/if}
      <button class="icon-btn" aria-label="Dismiss" onclick={() => app.dismiss(t.id)}><Icon name="x" size={14} /></button>
    </div>
  {/each}
</div>

<style>
  .toasts { position: fixed; right: 16px; bottom: calc(var(--statusbar-h) + 14px); display: grid; gap: 8px; z-index: 60; width: min(420px, calc(100vw - 32px)); }
  .toast { display: flex; align-items: flex-start; gap: 10px; padding: 10px 10px 10px 14px; border-radius: var(--radius);
    background: var(--bg-raised); border: 1px solid var(--line-strong); box-shadow: var(--shadow); font-size: 14px; animation: rise .2s var(--ease); }
  .toast > span { flex: 1; padding-top: 1px; }
  .toast :global(.icon) { margin-top: 3px; }
  .toast.ok :global(.icon) { color: var(--ok); }
  .toast.warn { border-color: color-mix(in srgb, var(--warn) 45%, transparent); }
  .toast.warn :global(.icon) { color: var(--warn); }
  .toast.error { border-color: color-mix(in srgb, var(--danger) 45%, transparent); }
  .toast.error :global(.icon:first-child) { color: var(--danger); }
  @keyframes rise { from { transform: translateY(8px); opacity: 0; } }
</style>
