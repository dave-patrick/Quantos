<script lang="ts">
  import { flushSync, tick } from 'svelte';
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { openNode, whereFrom } from '../lib/links.svelte';
  import { cellValue, display, isBuiltin, linkTarget, type QueryRow } from '../lib/query';
  import type { NodeDto } from '../lib/types';

  /**
   * Editable data view spanning notes (BUILD-PLAN 8C.6, 8C.11): each cell is a property of a
   * different node; edits save through the single write path. Spreadsheet keyboard navigation:
   * arrows move, Enter or typing edits, Escape cancels, Tab moves right (and out after the last column).
   */
  let { rows, columns, groups = null, readonly = false, onchanged }:
    { rows: QueryRow[]; columns: string[]; groups?: [string, QueryRow[]][] | null; readonly?: boolean; onchanged?: () => void } = $props();

  let active = $state<{ r: number; c: number } | null>(null);
  let editing = $state(false);
  let draft = $state('');
  let table = $state<HTMLTableElement | null>(null);

  const flat = $derived(groups ? groups.flatMap(([, list]) => list) : rows);

  function headerLabel(c: string) { return c === 'title' ? 'Name' : c.replace(/_/g, ' '); }

  async function focusCell(r: number, c: number) {
    active = { r: Math.max(0, Math.min(r, flat.length - 1)), c: Math.max(0, Math.min(c, columns.length - 1)) };
    await tick();
    table?.querySelector<HTMLElement>(`[data-cell="${active.r}-${active.c}"]`)?.focus();
  }

  function startEdit(initial?: string) {
    if (!active || readonly) return;
    const col = columns[active.c];
    if (isBuiltin(col) && col !== 'title' && col !== 'tags') return;
    // Render the input now so fast typing after the first key isn't lost.
    flushSync(() => { editing = true; draft = initial ?? display(cellValue(flat[active!.r], col)); });
    const input = table?.querySelector<HTMLInputElement>('input.edit');
    input?.focus();
    if (initial === undefined) input?.select();
  }

  async function commit(move: 'down' | 'right' | null) {
    if (!active || !editing) return;
    const value0 = draft;
    editing = false;
    const row = flat[active.r];
    const col = columns[active.c];
    const before = cellValue(row, col);
    const draftValue = value0;
    if (draftValue === display(before)) { if (move) await nav(move); return; }
    try {
      if (col === 'title') await api.post<NodeDto>(`nodes/${row.id}/rename`, { title: draftValue });
      else {
        const value: unknown = col === 'tags' || Array.isArray(before) ? draftValue.split(',').map((s) => s.trim()).filter(Boolean)
          : typeof before === 'number' && draftValue.trim() !== '' && !Number.isNaN(Number(draftValue)) ? Number(draftValue)
          : typeof before === 'boolean' ? draftValue === 'true' : draftValue;
        await api.patch(`nodes/${row.id}`, draftValue.trim() === '' && col !== 'tags' ? { remove: [col] } : { set: { [col]: value } });
      }
      onchanged?.();
    } catch (e) { app.error(e); }
    if (move) await nav(move);
  }

  async function nav(dir: 'up' | 'down' | 'left' | 'right') {
    if (!active) return;
    const { r, c } = active;
    if (dir === 'up') await focusCell(r - 1, c);
    if (dir === 'down') await focusCell(r + 1, c);
    if (dir === 'left') await focusCell(r, c - 1);
    if (dir === 'right') await focusCell(r, c + 1);
  }

  function onCellKey(e: KeyboardEvent) {
    if (editing) return;
    const keys: Record<string, 'up' | 'down' | 'left' | 'right'> = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' };
    if (keys[e.key]) { e.preventDefault(); nav(keys[e.key]); return; }
    if (e.key === 'Tab') {
      // Past the first or last column, Tab leaves the table like any other control.
      const edge = e.shiftKey ? active?.c === 0 : active?.c === columns.length - 1;
      if (edge) return;
      e.preventDefault(); nav(e.shiftKey ? 'left' : 'right'); return;
    }
    if (e.key === 'Enter' || e.key === 'F2') { e.preventDefault(); if (e.ctrlKey && active) openNode(flat[active.r].id, 'new'); else startEdit(); return; }
    if (e.key === 'Home') { e.preventDefault(); focusCell(active?.r ?? 0, 0); return; }
    if (e.key === 'End') { e.preventDefault(); focusCell(active?.r ?? 0, columns.length - 1); return; }
    if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) { e.preventDefault(); startEdit(e.key); }
  }

  function onEditKey(e: KeyboardEvent) {
    e.stopPropagation(); // the cell must not see keys meant for the editor
    if (e.key === 'Escape') { e.preventDefault(); editing = false; focusCell(active!.r, active!.c); }
    if (e.key === 'Enter') { e.preventDefault(); commit('down'); }
    if (e.key === 'Tab') { e.preventDefault(); commit('right'); }
  }

  function indexOf(row: QueryRow) { return flat.indexOf(row); }
</script>

<div class="wrap">
  <table bind:this={table} role="grid">
    <thead><tr>{#each columns as c (c)}<th scope="col">{headerLabel(c)}</th>{/each}</tr></thead>
    {#each groups ?? [['', rows] as [string, QueryRow[]]] as [name, list] (name)}
      <tbody>
        {#if groups}<tr class="group"><th colspan={columns.length}>{name || '—'} <span class="faint">{list.length}</span></th></tr>{/if}
        {#each list as row (row.id)}
          {@const r = indexOf(row)}
          <tr>
            {#each columns as col, c (col)}
              {@const value = cellValue(row, col)}
              <td data-cell="{r}-{c}" tabindex={active?.r === r && active?.c === c ? 0 : -1} class:active={active?.r === r && active?.c === c}
                onfocus={() => (active = { r, c })} onclick={() => focusCell(r, c)} ondblclick={() => startEdit()} onkeydown={onCellKey}>
                {#if editing && active?.r === r && active?.c === c}
                  <input class="edit" bind:value={draft} onkeydown={onEditKey} onblur={() => commit(null)} />
                {:else if col === 'title'}
                  <button class="open" tabindex="-1" onclick={(e) => { e.stopPropagation(); openNode(row.id, whereFrom(e), row.title); }}>{row.title}</button>
                {:else if linkTarget(value)}
                  <button class="link" tabindex="-1" onclick={(e) => { e.stopPropagation(); openNode(linkTarget(value)!, whereFrom(e)); }}>{display(value)}</button>
                {:else if typeof value === 'boolean'}
                  <input type="checkbox" checked={value} disabled={readonly} tabindex="-1"
                    onchange={async (e) => { await api.patch(`nodes/${row.id}`, { set: { [col]: (e.target as HTMLInputElement).checked } }).catch((x) => app.error(x)); onchanged?.(); }} />
                {:else}
                  <span class:faint={col === 'modified' || col === 'created' || col === 'path'}>{col === 'modified' || col === 'created' ? display(value).slice(0, 16).replace('T', ' ') : display(value)}</span>
                {/if}
              </td>
            {/each}
          </tr>
        {/each}
      </tbody>
    {/each}
  </table>
  {#if flat.length === 0}<p class="faint empty">No matching notes.</p>{/if}
</div>

<style>
  .wrap { overflow: auto; border: 1px solid var(--line); border-radius: 8px; }
  table { border-collapse: collapse; width: 100%; font-size: 13.5px; }
  th { text-align: left; font: 600 11px var(--font-mono); letter-spacing: .06em; text-transform: uppercase; color: var(--text-faint); padding: 7px 10px; background: var(--bg-raised); border-bottom: 1px solid var(--line); position: sticky; top: 0; white-space: nowrap; }
  tr.group th { text-transform: none; font: 700 13px var(--font-body); color: var(--text-soft); background: var(--bg-panel); position: static; }
  td { padding: 0 10px; height: calc(34px * var(--d)); border-bottom: 1px solid var(--line); max-width: 320px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; outline: none; }
  td + td { border-left: 1px solid var(--line); }
  td.active { box-shadow: inset 0 0 0 2px var(--accent); }
  tr:hover td { background: color-mix(in srgb, var(--bg-hover) 60%, transparent); }
  .edit { width: 100%; height: 28px; border: 0; background: var(--bg-input); color: var(--text); padding: 0 4px; font: inherit; }
  .edit:focus { outline: none; box-shadow: none; }
  .open { border: 0; background: none; padding: 0; color: var(--text); font-weight: 600; text-align: left; max-width: 100%; overflow: hidden; text-overflow: ellipsis; }
  .open:hover { color: var(--accent); text-decoration: underline; }
  .link { border: 0; background: none; padding: 0; color: var(--accent); }
  .empty { padding: 14px; margin: 0; }
</style>
