import { describe, expect, it } from 'vitest';
import { normalizeSidebarCollapsed, toggleSidebarCollapsed } from '../src/lib/sidebarCollapse';

describe('sidebar section state', () => {
  it('restores saved choices and uses defaults for missing values', () => {
    expect(normalizeSidebarCollapsed({ planning: false, folders: true })).toMatchObject({ planning: false, explore: true, folders: true });
    expect(normalizeSidebarCollapsed(null)).toMatchObject({ planning: true, explore: true });
  });
  it('toggles a group without changing other saved choices', () => {
    expect(toggleSidebarCollapsed({ planning: true, folders: false }, 'folders')).toEqual({ planning: true, folders: true });
  });
});
