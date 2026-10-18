import { describe, expect, it } from 'vitest';
import { fromJson, toJson, ruleProblems, blankRule } from '../src/components/AutomationEditor.svelte';

const example = [{
  name: 'Stamp finished projects',
  when: { tag: 'project', property: 'status', to: 'Done' },
  then: [
    { set: { finished_on: '{{today}}', reviewed: 'no' } },
    { createTask: { title: 'Write a retro for {{title}}', due: '{{today+7}}' } },
    { notify: '{{title}} is finished' }
  ]
}];

describe('automation form', () => {
  it('turns each part of an action into its own row and back', () => {
    const rules = fromJson(example);
    expect(rules[0].then.map((a) => a.kind)).toEqual(['set', 'set', 'createTask', 'notify']);
    expect(toJson(rules)).toEqual([{
      name: 'Stamp finished projects',
      when: { type: undefined, tag: 'project', property: 'status', from: undefined, to: 'Done' },
      then: [
        { set: { finished_on: '{{today}}' } }, { set: { reviewed: 'no' } },
        { createTask: { title: 'Write a retro for {{title}}', due: '{{today+7}}' } },
        { notify: '{{title}} is finished' }
      ]
    }]);
  });
  it('keeps a turned-off rule off and strips # from tags', () => {
    const rule = { ...blankRule(), name: 'Tag done', enabled: false };
    rule.when.tag = '#project';
    rule.then = [{ kind: 'addTag' as const, key: '', value: '#done', due: '' }];
    expect(toJson([rule])[0]).toMatchObject({ enabled: false, when: { tag: 'project' }, then: [{ addTag: 'done' }] });
  });
  it('says what is missing before saving', () => {
    const rule = { ...blankRule(), then: [] };
    rule.when.property = '';
    expect(ruleProblems([rule])).toEqual(['Automation 1: give it a name.', 'Automation 1: choose the property to watch.', 'Automation 1: add at least one action.']);
  });
});
