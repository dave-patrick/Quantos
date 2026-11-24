<script lang="ts">
  import { classifyRecurrence, describeRecurrence } from '../lib/recurrence';
  import { editRecurrence } from '../lib/recurrenceDialog.svelte';
  import Icon from './Icon.svelte';

  let {
    value = '',
    scheduled = null,
    ariaLabel = 'Recurrence',
    variant = 'cell',
    disabled = false,
    onchange
  }: {
    value?: string | null;
    scheduled?: string | null;
    ariaLabel?: string;
    variant?: 'cell' | 'input';
    disabled?: boolean;
    onchange: (next: string | null) => void;
  } = $props();

  let selectEl = $state<HTMLSelectElement | null>(null);
  const preset = $derived(classifyRecurrence(value));
  const customLabel = $derived(preset === 'custom' && value ? `Custom (${describeRecurrence(value)})` : 'Custom…');

  async function openCustom() {
    if (disabled) return;
    const next = await editRecurrence({ value: value || null, scheduled });
    if (next !== undefined) onchange(next);
  }

  async function onSelect(e: Event) {
    const target = e.target as HTMLSelectElement;
    const choice = target.value;
    if (choice === 'custom') {
      target.value = preset;
      await openCustom();
      return;
    }
    onchange(choice === 'none' ? null : choice);
  }
</script>

<div class="recurrence-picker">
  <select bind:this={selectEl} class={variant} aria-label={ariaLabel} value={preset} {disabled} onchange={onSelect}>
    <option value="none">Does not repeat</option>
    <option value="daily">Daily</option>
    <option value="weekdays">Weekdays (Mon–Fri)</option>
    <option value="weekly">Weekly</option>
    <option value="monthly">Monthly</option>
    <option value="yearly">Annually</option>
    <option value="custom">{customLabel}</option>
  </select>
  {#if preset !== 'none'}
    <button type="button" class="edit-btn" {disabled} title="Customise recurrence" aria-label="Customise recurrence" onclick={openCustom}>
      <Icon name="sliders" size={13} />
    </button>
  {/if}
</div>

<style>
  .recurrence-picker { flex: 1; min-width: 0; display: flex; align-items: center; gap: 4px; }
  select { flex: 1; min-width: 0; text-overflow: ellipsis; }
  .cell { height: calc(28px * var(--d)); padding: 0 8px; border-radius: 5px; border: 1px solid transparent; background: transparent; color: var(--text); font-size: 14px; }
  .cell:hover:not(:disabled) { border-color: var(--line); }
  .cell:focus { outline: none; border-color: var(--accent); background: var(--bg-input); }
  .edit-btn { border: 0; background: none; color: var(--text-muted); padding: 4px 6px; border-radius: 5px; display: inline-grid; place-items: center; flex: none; }
  .edit-btn:hover:not(:disabled) { background: var(--bg-hover); color: var(--accent); }
</style>
