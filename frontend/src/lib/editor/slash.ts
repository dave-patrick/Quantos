import type { Completion, CompletionContext, CompletionResult } from '@codemirror/autocomplete';
import type { EditorView } from '@codemirror/view';

/**
 * The slash menu: type "/" and a list of things to put in the note appears. The menu is a plain
 * completion list, so the arrow keys, Enter and Escape work as they do everywhere else.
 */
export interface SlashCommand {
  id: string;
  title: string;
  /** What it puts in, shown next to the title. */
  hint?: string;
  group: string;
  /** The text it inserts, or the work it does. */
  apply: (view: EditorView, from: number, to: number) => void;
}

const insert = (text: string, place: (text: string) => string = (t) => t) => (view: EditorView, from: number, to: number) => {
  view.dispatch({ changes: { from, to, insert: place(text) }, selection: { anchor: from + place(text).length } });
  view.focus();
};

const today = new Date();
const iso = (d: Date) => d.toISOString().slice(0, 10);

export const slashCommands: SlashCommand[] = [
  { id: 'task', title: 'Task', hint: '- [ ] ', group: 'Blocks', apply: insert('- [ ] ') },
  { id: 'bullet', title: 'Bullet list', hint: '- ', group: 'Blocks', apply: insert('- ') },
  { id: 'number', title: 'Numbered list', hint: '1. ', group: 'Blocks', apply: insert('1. ') },
  { id: 'quote', title: 'Quote', hint: '> ', group: 'Blocks', apply: insert('> ') },
  { id: 'callout-note', title: 'Callout: note', hint: '> [!note]', group: 'Blocks', apply: insert('> [!note] ') },
  { id: 'callout-tip', title: 'Callout: tip', hint: '> [!tip]', group: 'Blocks', apply: insert('> [!tip] ') },
  { id: 'callout-warning', title: 'Callout: warning', hint: '> [!warning]', group: 'Blocks', apply: insert('> [!warning] ') },
  { id: 'heading1', title: 'Heading 1', hint: '# ', group: 'Blocks', apply: insert('# ') },
  { id: 'heading2', title: 'Heading 2', hint: '## ', group: 'Blocks', apply: insert('## ') },
  { id: 'heading3', title: 'Heading 3', hint: '### ', group: 'Blocks', apply: insert('### ') },
  { id: 'code', title: 'Code block', hint: '```', group: 'Blocks', apply: insert('```\n') },
  { id: 'table', title: 'Table', hint: '| | |', group: 'Blocks', apply: insert('| | |\n|---|---|\n| | |') },
  { id: 'divider', title: 'Divider', hint: '---', group: 'Blocks', apply: insert('\n---\n') },
  { id: 'date', title: "Today's date", hint: iso(today), group: 'Text', apply: insert(iso(today)) },
  {
    id: 'time',
    title: 'Current time',
    hint: new Date().toTimeString().slice(0, 5),
    group: 'Text',
    apply: insert(new Date().toTimeString().slice(0, 5))
  }
];

/** The "/" has to start a line or follow a space, so paths and addresses are left alone. */
function slashBefore(context: CompletionContext): { from: number; query: string } | null {
  const before = context.matchBefore(/\/([\w-]*)$/);
  if (!before) return null;
  const line = context.state.doc.lineAt(before.from);
  const start = line.text.slice(0, before.from - line.from);
  if (start.length > 0 && !/\s$/.test(start)) return null;
  return { from: before.from, query: before.text.slice(1) };
}

/** The completion source that shows the slash menu. */
export function slashCompletion(): (context: CompletionContext) => CompletionResult | null {
  return (context: CompletionContext): CompletionResult | null => {
    const found = slashBefore(context);
    if (!found) return null;
    const options: Completion[] = slashCommands
      .filter((c) => !found.query || c.title.toLowerCase().includes(found.query.toLowerCase()))
      .map((c) => ({
        label: c.title,
        type: 'text',
        detail: c.hint,
        boost: c.title.toLowerCase().startsWith(found.query.toLowerCase()) ? 1 : 0,
        apply: (view: EditorView, _completion: Completion, from: number, to: number) => c.apply(view, found.from, to)
      }));
    if (!options.length) return null;
    return { from: found.from, options, filter: false };
  };
}
