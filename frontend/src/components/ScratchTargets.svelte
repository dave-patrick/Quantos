<script lang="ts">
  import { app } from '../lib/state.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { displayKeys, normalizeKey } from '../lib/commands.svelte';
  import { DEFAULT_SCRATCH_TARGETS, KIND_LABELS, type ScratchTarget } from '../lib/scratch';
  import Icon from './Icon.svelte';

  /** Settings › Shortcuts › Scratchpad: where each "send to" key puts text. */
  const targets = $derived(prefs.scratchTargets);
  function update(i: number, patch: Partial<ScratchTarget>) {
    prefs.scratchTargets = targets.map((t, j) => (j === i ? { ...t, ...patch } : t));
  }
  function remove(i: number) { prefs.scratchTargets = targets.filter((_, j) => j !== i); }
  function add() {
    prefs.scratchTargets = [...targets, { id: crypto.randomUUID().slice(0, 8), label: 'A note', keys: '', kind: 'note' }];
  }
  async function chooseNote(i: number) {
    const n = await app.pickNote('Send scratchpad text to which note?', { confirmLabel: 'Choose' });
    if (n) update(i, { noteId: n.id, noteTitle: n.title, label: targets[i].label === 'A note' || !targets[i].label ? n.title : targets[i].label });
  }
  function record(e: KeyboardEvent, i: number) {
    if (e.key === 'Tab' || e.key === 'Escape') return;
    e.preventDefault();
    if (e.key === 'Backspace' || e.key === 'Delete') { update(i, { keys: '' }); return; }
    const combo = normalizeKey(e);
    if (!combo.includes('+') || /^(Mod|Alt|Shift)(\+(Mod|Alt|Shift))*$/.test(combo)) return;
    update(i, { keys: combo });
  }
  const clash = (i: number) => !!targets[i].keys && targets.some((t, j) => j !== i && t.keys === targets[i].keys);
</script>

<h3>Scratchpad: send to</h3>
<p class="faint small">In the scratchpad, these keys move the selected text (or the paragraph the cursor is in) to a place, and take it out of the scratchpad. Click a keys box and press the combination; Backspace clears it.</p>
<div class="targets">
  {#each targets as t, i (t.id)}
    <div class="target">
      <input class="input name" aria-label="Name" value={t.label} onchange={(e) => update(i, { label: (e.target as HTMLInputElement).value })} />
      <select class="input" aria-label="Destination" value={t.kind} onchange={(e) => update(i, { kind: (e.target as HTMLSelectElement).value as ScratchTarget['kind'] })}>
        {#each Object.entries(KIND_LABELS) as [k, l] (k)}<option value={k}>{l}</option>{/each}
      </select>
      {#if t.kind === 'note'}
        <button class="btn ghost" onclick={() => chooseNote(i)}><Icon name="file-text" size={13} /> {t.noteTitle ?? 'Choose note…'}</button>
        <input class="input heading" aria-label="Under heading (optional)" placeholder="Under heading (optional)" value={t.heading ?? ''} onchange={(e) => update(i, { heading: (e.target as HTMLInputElement).value.trim() || undefined })} />
      {/if}
      <input class="input keys" class:bad={clash(i)} aria-label={`Keys for ${t.label}`} readonly value={t.keys ? displayKeys(t.keys) : ''} placeholder="Press keys" onkeydown={(e) => record(e, i)} title={clash(i) ? 'Another destination uses these keys' : ''} />
      <button class="icon-btn" aria-label={`Remove ${t.label}`} onclick={() => remove(i)}><Icon name="x" size={14} /></button>
    </div>
  {/each}
</div>
<div class="row">
  <button class="btn ghost" onclick={add}><Icon name="plus" size={14} /> Add a destination</button>
  <button class="btn ghost" onclick={() => (prefs.scratchTargets = DEFAULT_SCRATCH_TARGETS.map((t) => ({ ...t })))}>Reset</button>
</div>

<style>
  h3 { font: 700 16px/1.3 var(--font-display); margin: 22px 0 6px; }
  .small { font-size: 12.5px; max-width: 680px; }
  .targets { display: grid; gap: 6px; margin: 8px 0; }
  .target { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
  .target .input { width: auto; }
  .name { width: 180px !important; }
  .heading { width: 170px !important; }
  .keys { width: 150px !important; font-family: var(--font-mono); cursor: pointer; }
  .keys.bad { border-color: var(--warn); }
  .row { display: flex; gap: 6px; }
</style>
