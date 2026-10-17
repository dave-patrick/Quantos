<script lang="ts">
  import { app } from '../lib/state.svelte';
  import { modal } from '../lib/modal';
  import type { NodeSummary } from '../lib/types';

  /** The text question and note picker behind app.ask and app.pickNote. */
  const spec = $derived(app.asking);
  let value = $state('');
  let active = $state(0);
  let field = $state<HTMLInputElement | null>(null);

  $effect(() => {
    if (!spec) return;
    value = spec.value ?? '';
    active = 0;
    queueMicrotask(() => { field?.focus(); field?.select(); });
  });

  // Same titles can live in several places, so each match shows where it is.
  const matches = $derived.by((): NodeSummary[] => {
    if (!spec?.notes) return [];
    const q = value.trim().toLowerCase();
    const list = app.titles.filter((t) => t.id !== spec.exclude && t.type !== 'supertag' && t.type !== 'template');
    if (!q) return list.slice(0, 8);
    const starts = list.filter((t) => t.title.toLowerCase().startsWith(q));
    const contains = list.filter((t) => !t.title.toLowerCase().startsWith(q) && t.title.toLowerCase().includes(q));
    return [...starts, ...contains].slice(0, 8);
  });

  function where(n: NodeSummary) {
    // workspaces/<ws>/nodes/client_work/note.md shows as "<ws> · client work".
    const parts = n.path.split('/').slice(0, -1);
    const inside = parts[0] === 'workspaces' ? parts.slice(2) : parts;
    const folder = inside.filter((p, i) => !(i === 0 && ['nodes', 'tasks', 'daily'].includes(p))).map((p) => p.replace(/_/g, ' ')).join(' › ');
    return [n.workspace, folder].filter(Boolean).join(' · ') + (n.archived ? ' · archived' : '');
  }

  function submit() {
    if (!spec) return;
    if (spec.notes) { const pick = matches[active]; if (pick) spec.done(pick.id); return; }
    if (!value.trim()) return;
    if (spec.numeric && Number.isNaN(Number(value))) return;
    spec.done(value);
  }

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Enter') { e.preventDefault(); submit(); }
    else if (spec?.notes && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
      e.preventDefault();
      if (matches.length) active = (active + (e.key === 'ArrowDown' ? 1 : -1) + matches.length) % matches.length;
    }
  }
</script>

{#if spec}
  <div class="scrim">
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="ask-title" use:modal={{ onclose: () => spec.done(null) }}>
      <h2 id="ask-title">{spec.title}</h2>
      {#if spec.message}<p>{spec.message}</p>{/if}
      <input class="input" bind:this={field} bind:value {onkeydown} oninput={() => (active = 0)} placeholder={spec.placeholder ?? ''}
        inputmode={spec.numeric ? 'numeric' : undefined} aria-label={spec.title}
        role={spec.notes ? 'combobox' : undefined} aria-expanded={spec.notes ? matches.length > 0 : undefined}
        aria-controls={spec.notes ? 'ask-matches' : undefined} aria-activedescendant={spec.notes && matches[active] ? `ask-${matches[active].id}` : undefined} />
      {#if spec.notes}
        <div class="matches" id="ask-matches" role="listbox" aria-label="Matching notes">
          {#each matches as n, i (n.id)}
            <button id="ask-{n.id}" role="option" aria-selected={i === active} class:on={i === active} tabindex="-1"
              onmouseenter={() => (active = i)} onclick={() => spec.done(n.id)}>
              <span class="t">{n.title}</span><span class="w faint">{where(n)}</span>
            </button>
          {:else}
            <p class="faint none">No note matches.</p>
          {/each}
        </div>
      {/if}
      <div class="actions">
        <button class="btn" onclick={() => spec.done(null)}>Cancel</button>
        <button class="btn primary" onclick={submit} disabled={spec.notes ? !matches.length : !value.trim()}>{spec.confirmLabel ?? 'OK'}</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; background: rgb(4 8 14 / .6); display: grid; place-items: center; z-index: 80; padding: 16px; }
  .dialog { width: min(480px, 100%); background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 10px; box-shadow: var(--shadow); padding: 20px 22px; display: flex; flex-direction: column; gap: 12px; }
  h2 { font: 700 18px/1.3 var(--font-display); }
  p { color: var(--text-soft); white-space: pre-wrap; }
  .input { width: 100%; }
  .matches { display: flex; flex-direction: column; max-height: 280px; overflow: auto; margin: -4px 0; }
  .matches button { display: flex; align-items: baseline; gap: 10px; text-align: left; padding: 6px 8px; border-radius: 6px; }
  .matches button.on { background: var(--accent-wash); }
  .t { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .w { font-size: 12px; white-space: nowrap; }
  .none { font-size: 13px; padding: 6px 8px; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
