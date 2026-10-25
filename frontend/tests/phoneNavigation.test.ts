import { describe, expect, it } from 'vitest';
import { normalizePhoneNavigation } from '../src/lib/phoneNavigation';

describe('phone navigation preferences', () => {
  it('falls back safely for missing or unusable stored preferences', () => {
    for (const value of [null, {}, [], ['removed-feature'], ['more']]) {
      expect(normalizePhoneNavigation(value)).toEqual(['daily', 'tasks', 'capture', 'myday']);
    }
  });
  it('preserves a chosen order and rejects duplicate or invalid destinations', () => {
    expect(normalizePhoneNavigation(['daily', null, 'daily', 'settings', 'node', 'tasks'])).toEqual(['daily', 'settings', 'tasks']);
  });
  it('limits shortcuts to four so More still fits', () => {
    expect(normalizePhoneNavigation(['daily', 'search', 'tasks', 'focus', 'notes'])).toEqual(['daily', 'search', 'tasks', 'focus']);
  });
  it('retains quick capture as a selectable action alongside pages', () => {
    expect(normalizePhoneNavigation(['capture', 'daily', 'tasks', 'myday'])).toEqual(['capture', 'daily', 'tasks', 'myday']);
  });
});
