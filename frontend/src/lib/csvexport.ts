import { api } from './api';
import { cellValue, display, toCsv, type QueryRow } from './query';
import { app } from './state.svelte';

/**
 * Saves a table's rows as CSV in the vault's exports folder. Columns are property keys, so the
 * file can be imported back; links read as titles and lists as comma-separated values.
 */
export async function exportRowsAsCsv(name: string, columns: string[], rows: QueryRow[]) {
  const csv = toCsv(columns, rows.map((r) => columns.map((c) => display(cellValue(r, c)))));
  try {
    const result = await api.post<{ path: string }>('export/table', { name, csv });
    app.toast(`Exported ${rows.length} row(s) to ${result.path}`, 'ok');
  } catch (e) { app.error(e); }
}
