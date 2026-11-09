import { expect, it } from 'vitest';
import { newTagScope } from '../src/lib/tagScope';
it('defaults to the note workspace and permits explicit sharing', () => {
  expect(newTagScope(undefined, 'videos', ['default', 'videos'])).toBe('videos');
  expect(newTagScope('workspace', 'default', ['default'])).toBe('default');
  expect(newTagScope('shared', 'videos', ['videos'])).toBe('');
});
it('keeps definitions out of folders owned by other apps', () => {
  expect(newTagScope('workspace', 'linked-folder', ['default', 'videos'])).toBe('');
});
