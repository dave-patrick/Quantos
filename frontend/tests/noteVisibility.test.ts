import { expect, it } from 'vitest';
import { isNoteContent, isNoteFolder } from '../src/lib/noteVisibility';
it('keeps tagged content and other user records while excluding definitions', () => {
  const records = ['note', 'task', 'person', 'project', 'daily', 'canvas', 'event', 'template', 'supertag', 'synced_block'].map(type => ({ type, tags: ['meeting'] }));
  expect(records.filter(isNoteContent).map(n => n.type)).toEqual(['note', 'task', 'person', 'project', 'daily', 'canvas', 'event', 'template']);
});
it('hides the managed definition directory but preserves similarly named user folders', () => {
  expect(isNoteFolder('workspaces/videos/supertags')).toBe(false);
  expect(isNoteFolder('workspaces/videos/supertags/nested')).toBe(false);
  expect(isNoteFolder('workspaces/videos/nodes/supertags')).toBe(true);
  expect(isNoteFolder('linked/videos/supertags')).toBe(true);
});
