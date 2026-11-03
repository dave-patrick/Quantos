import { describe, expect, it } from 'vitest';
import { directionLabel, statusLabel, syncLine, validateMappings, type GoogleStatus } from '../src/lib/googleTasks';

const base: GoogleStatus = {
  state: 'Connected', hasCredentials: true, flow: 'idle', mappings: [], intervalSeconds: 120, lastSynced: null, nextAttempt: null, error: null,
  counts: { linked: 0, pushed: 0, pulled: 0, skipped: 0, pendingDeletes: 0 }, pendingDeletes: []
};

describe('Google Tasks settings rules', () => {
  it('accepts empty and valid mappings', () => {
    expect(validateMappings([])).toBeNull();
    expect(validateMappings([{ workspace: 'default', listId: 'L1', listTitle: 'Mine' }, { workspace: 'work', listId: 'L2', listTitle: 'Work' }])).toBeNull();
  });

  it('rejects an incomplete row, a repeated workspace and a repeated list', () => {
    expect(validateMappings([{ workspace: 'default', listId: '', listTitle: '' }])).toMatch(/Choose a workspace/);
    expect(validateMappings([{ workspace: '', listId: 'L1', listTitle: 'x' }])).toMatch(/Choose a workspace/);
    expect(validateMappings([{ workspace: 'a', listId: 'L1', listTitle: 'One' }, { workspace: 'a', listId: 'L2', listTitle: 'Two' }])).toMatch(/“a” is used twice/);
    expect(validateMappings([{ workspace: 'a', listId: 'L1', listTitle: 'One' }, { workspace: 'b', listId: 'L1', listTitle: 'One' }])).toMatch(/“One” is used twice/);
  });

  it('lets several rows each ask for a new list', () => {
    expect(validateMappings([{ workspace: 'a', listId: '__new__', listTitle: '' }, { workspace: 'b', listId: '__new__', listTitle: '' }])).toBeNull();
    expect(validateMappings([{ workspace: 'a', listId: '__new__', listTitle: '' }, { workspace: 'a', listId: '__new__', listTitle: '' }])).toMatch(/“a” is used twice/);
  });

  it('describes each state in plain words', () => {
    expect(statusLabel(null)).toBe('Loading…');
    expect(statusLabel({ ...base, state: 'Off', hasCredentials: false })).toMatch(/Not set up/);
    expect(statusLabel({ ...base, state: 'Off' })).toMatch(/Press Connect/);
    expect(statusLabel({ ...base, state: 'NeedsReconnect' })).toMatch(/sign in again/);
    expect(statusLabel({ ...base, state: 'Paused', reason: 'Open the vault this was set up for' })).toBe('Open the vault this was set up for');
    expect(statusLabel(base)).toBe('Connected to Google Tasks.');
  });

  it('summarises the last cycle, a pending retry, and the unconfirmed case', () => {
    const now = new Date('2026-10-30T12:00:00');
    expect(syncLine({ ...base, state: 'Off' }, now)).toBe('');
    expect(syncLine(base, now)).toMatch(/Review a list below/);
    const confirmed = { ...base, mappings: [{ workspace: 'a', listId: 'L', listTitle: 'x', confirmed: true, state: 'Ok' as const }] };
    expect(syncLine(confirmed, now)).toBe('Waiting for the first sync.');
    expect(syncLine({ ...confirmed, lastSynced: '2026-10-30T11:58:00', counts: { ...base.counts, pushed: 3, pulled: 1, skipped: 2 } }, now)).toMatch(/3 sent · 1 received · 2 left alone/);
    expect(syncLine({ ...confirmed, nextAttempt: '2026-10-30T12:05:00' }, now)).toMatch(/try again/);
  });

  it('says what each pending deletion will do', () => {
    expect(directionLabel('toGoogle')).toMatch(/deleted in Google/);
    expect(directionLabel('toQuantos')).toMatch(/Trash/);
  });
});
