/** Google Tasks sync: the shapes the backend returns, and the small pure rules the settings page uses. */

export interface GoogleMapping { workspace: string; listId: string; listTitle: string; confirmed: boolean; state: 'Ok' | 'Paused'; reason?: string | null }
export interface GooglePending { taskId: string; title: string; direction: 'toGoogle' | 'toQuantos' }
export interface GoogleStatus {
  state: 'Off' | 'Connected' | 'NeedsReconnect' | 'Paused';
  reason?: string | null;
  hasCredentials: boolean;
  /** This copy of Quantos has a Google client built in, so there is nothing to set up before Connect. */
  builtInClient?: boolean;
  flow: 'idle' | 'waiting';
  mappings: GoogleMapping[];
  intervalSeconds: number;
  lastSynced?: string | null;
  nextAttempt?: string | null;
  error?: string | null;
  /** Something the person should know that is not an error, such as access that could not be revoked. */
  notice?: string | null;
  counts: { linked: number; pushed: number; pulled: number; skipped: number; pendingDeletes: number };
  pendingDeletes: GooglePending[];
  problems?: { workspace: string; reason: string }[] | null;
}
export interface GooglePreview { cutoff: string; createInGoogle: number; importToQuantos: number; link: number; excluded: number }
export interface MappingRow { workspace: string; listId: string; listTitle: string }

/** The reason a set of rows cannot be saved, or null. Mirrors the checks the backend makes. */
export const NEW_LIST = '__new__';

export function validateMappings(rows: MappingRow[]): string | null {
  const workspaces = new Set<string>();
  const lists = new Set<string>();
  for (const row of rows) {
    if (!row.workspace || !row.listId) return 'Choose a workspace and a Google list for each row.';
    if (workspaces.has(row.workspace)) return `“${row.workspace}” is used twice.`;
    workspaces.add(row.workspace);
    // Several rows may each ask for a new list; those become different lists when saved.
    if (row.listId === NEW_LIST) continue;
    if (lists.has(row.listId)) return `“${row.listTitle || row.listId}” is used twice.`;
    lists.add(row.listId);
  }
  return null;
}

/** One plain line for the top of the page. */
export function statusLabel(status: GoogleStatus | null): string {
  if (!status) return 'Loading…';
  switch (status.state) {
    case 'Off': return status.hasCredentials ? 'Not connected. Press Connect to sign in to Google.' : 'Not set up. Add your Google client ID and secret to begin.';
    case 'NeedsReconnect': return 'Google needs you to sign in again.';
    case 'Paused': return status.reason ?? 'Paused.';
    default: return status.reason ?? 'Connected to Google Tasks.';
  }
}

/** “Last synced 14:02 · 3 sent · 1 received · 2 left alone”, or what is happening instead. */
export function syncLine(status: GoogleStatus | null, now = new Date()): string {
  if (!status || status.state !== 'Connected') return '';
  if (status.nextAttempt && new Date(status.nextAttempt) > now) return `Waiting to try again at ${new Date(status.nextAttempt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}.`;
  if (!status.lastSynced) return status.mappings.some((m) => m.confirmed) ? 'Waiting for the first sync.' : 'Review a list below to start syncing.';
  const c = status.counts;
  return `Last synced ${new Date(status.lastSynced).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} · ${c.pushed} sent · ${c.pulled} received · ${c.skipped} left alone`;
}

export const intervalChoices = [
  { seconds: 60, label: 'Every minute' },
  { seconds: 120, label: 'Every 2 minutes' },
  { seconds: 300, label: 'Every 5 minutes' },
  { seconds: 900, label: 'Every 15 minutes' }
];

export function directionLabel(d: GooglePending['direction']): string {
  return d === 'toGoogle' ? 'Deleted here — will be deleted in Google' : 'Deleted in Google — will move to Trash here';
}
