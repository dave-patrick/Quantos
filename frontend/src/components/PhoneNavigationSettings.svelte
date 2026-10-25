<script lang="ts">
  import { prefs } from '../lib/prefs.svelte';
  import { phoneDestinations, defaultPhoneNavigation, normalizePhoneNavigation } from '../lib/phoneNavigation';
  import Icon from './Icon.svelte';

  const selected = $derived(normalizePhoneNavigation(prefs.phoneNavigation));
  function move(index: number, step: number) {
    const items = [...selected];
    [items[index], items[index + step]] = [items[index + step], items[index]];
    prefs.phoneNavigation = items;
  }
</script>

<section class="phone-navigation-settings" aria-labelledby="phone-navigation-heading">
  <h2 id="phone-navigation-heading">Phone navigation</h2>
  <p>Choose one to four shortcuts, in the order you want them. More always stays at the end so every tool remains within reach.</p>
  <p class="muted">Changes apply immediately and stay on this device. Tablet navigation stays the same.</p>
  <ol aria-label="Bottom navigation shortcuts">
    {#each selected as kind, i (kind)}
      {@const item = phoneDestinations.find(d => d.kind === kind)!}
      <li>
        <span class="shortcut"><Icon name={item.icon} /><span>{item.label}</span></span>
        <div class="reorder">
          <button class="icon-btn" aria-label="Move {item.label} left" disabled={i === 0} onclick={() => move(i, -1)}><Icon name="arrow-left" /></button>
          <button class="icon-btn" aria-label="Move {item.label} right" disabled={i === selected.length - 1} onclick={() => move(i, 1)}><Icon name="arrow-right" /></button>
          <button class="icon-btn" aria-label="Remove {item.label} from navigation" disabled={selected.length === 1} onclick={() => prefs.phoneNavigation = selected.filter(k => k !== kind)}><Icon name="x" /></button>
        </div>
      </li>
    {/each}
  </ol>
  <label class="add-shortcut">Add shortcut
    <select class="input" value="" disabled={selected.length >= 4} onchange={(e) => {
      const kind = e.currentTarget.value;
      if (kind && selected.length < 4 && !selected.includes(kind)) prefs.phoneNavigation = [...selected, kind];
      e.currentTarget.value = '';
    }}>
      <option value="">{selected.length >= 4 ? 'Four shortcuts selected' : 'Choose a tool…'}</option>
      {#each phoneDestinations.filter(d => !selected.includes(d.kind)) as item (item.kind)}<option value={item.kind}>{item.label}</option>{/each}
    </select>
  </label>
  <p class="muted">Remove a shortcut to make room for another. Use the arrows to change its position.</p>
  <button class="btn" onclick={() => prefs.phoneNavigation = [...defaultPhoneNavigation]}>Reset to Daily notes, Tasks, Capture and Today</button>
</section>

<style>
  h2 { font: 700 24px/1.2 var(--font-display); margin-bottom: 16px; }
  p { margin: 12px 0; }
  ol { list-style: none; padding: 0; margin: 24px 0; }
  li { display: flex; align-items: center; gap: 8px; min-height: 64px; border-bottom: 1px solid var(--line); }
  .shortcut { display: flex; align-items: center; gap: 12px; flex: 1; min-width: 0; }
  .shortcut span { overflow-wrap: anywhere; }
  .shortcut :global(.icon) { flex-shrink: 0; color: var(--text-muted); }
  .reorder { display: flex; gap: 8px; flex-shrink: 0; }
  .reorder button { width: 48px; height: 48px; }
  .add-shortcut { display: grid; gap: 8px; }
  select { width: 100%; min-height: 48px; }
  .btn { min-height: 48px; white-space: normal; }
</style>
