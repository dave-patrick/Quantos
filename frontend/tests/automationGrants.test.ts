import { describe, expect, it } from 'vitest';
import { automationGrantRows } from '../src/lib/automationGrants';

describe('automation grants', () => {
  it('lets a newly saved archive rule request archive without losing its default content permissions', () => {
    const rows = automationGrantRows(['Archive finished tasks'], {});
    expect(rows).toEqual([{
      name: 'Archive finished tasks', permissions: ['Read', 'CreateEdit'], saved: true
    }]);
    expect([...rows[0].permissions, 'MoveArchive']).toEqual(['Read', 'CreateEdit', 'MoveArchive']);
  });

  it('preserves explicit restrictions and orphan grants after a rule is renamed or removed', () => {
    expect(automationGrantRows(['New name', 'Restricted', 'New name'], {
      'Old name': ['Read', 'MoveArchive'], Restricted: []
    })).toEqual([
      { name: 'New name', permissions: ['Read', 'CreateEdit'], saved: true },
      { name: 'Restricted', permissions: [], saved: true },
      { name: 'Old name', permissions: ['Read', 'MoveArchive'], saved: false }
    ]);
  });

  it('uses saved names rather than mutable editor names', () => {
    const savedNames = ['Original'];
    const draft = [{ name: 'Original' }];
    draft[0].name = 'Unsaved rename';
    expect(automationGrantRows(savedNames, {}).map((row) => row.name)).toEqual(['Original']);
  });

  it('treats object prototype names as ordinary automation names', () => {
    expect(automationGrantRows(['constructor'], {})).toEqual([
      { name: 'constructor', permissions: ['Read', 'CreateEdit'], saved: true }
    ]);
  });
});
