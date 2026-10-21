// @vitest-environment happy-dom
import { describe, expect, it } from 'vitest';
import { EditorSelection, EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { blockAt } from '../src/lib/editor/blockinfo';
import { headingOn, sectionOf } from '../src/lib/editor/headings';
import { runOf, selectionBlocks } from '../src/lib/editor/blockselect';
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

describe('collapsible headings', () => {
  it('finds the level of a heading', () => {
    const s = state('# One\n## Two\n');
    expect(headingOn(s, 1)?.level).toBe(1);
    expect(headingOn(s, 2)?.level).toBe(2);
    expect(headingOn(s, 1)?.from).toBe(2);
  });

  it('does not take a "#" in code for a heading', () => {
    const s = state('```\n# not a heading\n```\n');
    expect(headingOn(s, 2)).toBeNull();
  });

  it('folds a heading down to the next heading of the same level', () => {
    const s = state('# One\ntext\n## Two\nmore\n# Three\n');
    const section = sectionOf(s, 1);
    expect(section?.from).toBe(5);
    expect(s.doc.sliceString(section!.from, section!.to)).toBe('\ntext\n## Two\nmore');
  });

  it('keeps a deeper heading inside the section', () => {
    const s = state('## Two\n### Three\ntext\n');
    const section = sectionOf(s, 1);
    expect(s.doc.sliceString(section!.from, section!.to)).toBe('\n### Three\ntext');
  });

  it('has nothing to fold for a heading on its own', () => {
    expect(sectionOf(state('# One\n# Two\n'), 1)).toBeNull();
  });
});

describe('block selection', () => {
  it('sees a selection of whole lines as blocks', () => {
    const s = state('# One\ntwo\nthree\n');
    const run = runOf(s, { from: 0, to: 9 });
    expect(run?.fromLine).toBe(1);
    expect(run?.toLine).toBe(2);
  });

  it('does not see half a line as a block', () => {
    const s = state('# One\ntwo\n');
    expect(runOf(s, { from: 2, to: 12 })).toBeNull();
  });

  it('lists the blocks a selection covers', () => {
    const s = state('# One\n- two\n- three\n');
    const selection = EditorSelection.create([
      EditorSelection.range(0, 5),
      EditorSelection.range(6, 11)
    ]);
    const withSelection = EditorState.create({ doc: s.doc, selection, extensions: EditorState.allowMultipleSelections.of(true) });
    const blocks = selectionBlocks(withSelection);
    expect(blocks.map((b) => b.text)).toEqual(['One', 'two']);
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
