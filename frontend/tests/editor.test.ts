// @vitest-environment happy-dom
import { describe, expect, it } from 'vitest';
import { EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { blockAt } from '../src/lib/editor/blockinfo';
import { slashCommands } from '../src/lib/editor/slash';

const state = (text: string) => EditorState.create({ doc: text });

describe('block detection', () => {
  it('finds a task line and its task id', () => {
    const s = state('- [ ] Call the bank <!--task:01M5AZYBPFP1F42NEQCKXCPQG6-->\n');
    const b = blockAt(s, 3);
    expect(b.kind).toBe('task');
    expect(b.text).toContain('Call the bank');
    expect(b.taskId).toBe('01M5AZYBPFP1F42NEQCKXCPQG6');
    expect(b.checked).toBe(false);
  });

  it('tells a ticked task from an unticked one', () => {
    expect(blockAt(state('- [x] Done\n'), 3).checked).toBe(true);
  });

  it('finds headings, lists and quotes', () => {
    expect(blockAt(state('## A heading\n'), 4).kind).toBe('heading');
    expect(blockAt(state('## A heading\n'), 4).text).toBe('A heading');
    expect(blockAt(state('- an item\n'), 3).kind).toBe('list');
    expect(blockAt(state('> a quote\n'), 3).kind).toBe('quote');
    expect(blockAt(state('just a line\n'), 3).kind).toBe('text');
  });

  it('takes the whole fence for a code block', () => {
    const s = state('```powershell\nGet-Date\n```\n');
    const b = blockAt(s, 15);
    expect(b.kind).toBe('code');
    expect(b.text).toContain('Get-Date');
    expect(b.language).toBe('powershell');
  });
});

describe('slash commands', () => {
  it('offers the things a note is made of', () => {
    const titles = slashCommands.map((c) => c.title);
    expect(titles).toContain('Task');
    expect(titles).toContain('Heading 2');
    expect(titles).toContain('Code block');
    expect(titles).toContain('Table');
  });

  it('puts the text in the note when chosen', () => {
    const view = new EditorView({ state: state('hello') });
    const task = slashCommands.find((c) => c.id === 'task')!;
    task.apply(view, 0, 0);
    expect(view.state.doc.toString()).toBe('- [ ] hello');
    view.destroy();
  });
});
