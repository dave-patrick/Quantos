import { RangeSetBuilder, StateEffect, type Extension } from '@codemirror/state';
import { Decoration, EditorView, ViewPlugin, WidgetType, type DecorationSet, type ViewUpdate } from '@codemirror/view';

export interface TaskHooks {
  /** Toggle a bound task node (inline checkbox sync). Unbound checkboxes just edit the text. */
  toggleTask?(taskId: string, checked: boolean): void;
  taskInfo?(taskId: string): { status: string; due?: string | null; planned?: string | null; priority?: number | null; deleted?: boolean } | undefined;
  openTask?(taskId: string, event: MouseEvent): void;
  restoreTask?(taskId: string): void;
}

const TASK_LINE = /^(\s*[-*+]\s+)\[([ xX])\](\s+)(.*?)(\s*<!--task:([0-9A-HJKMNP-TV-Z]{26})-->)?\s*$/;

class CheckWidget extends WidgetType {
  constructor(readonly checked: boolean, readonly pos: number, readonly taskId: string | null) { super(); }
  eq(o: CheckWidget) { return o.checked === this.checked && o.pos === this.pos && o.taskId === this.taskId; }
  toDOM() {
    const box = document.createElement('input');
    box.type = 'checkbox';
    box.className = 'cm-task-check';
    box.checked = this.checked;
    box.dataset.pos = String(this.pos);
    if (this.taskId) box.dataset.task = this.taskId;
    box.setAttribute('aria-label', this.checked ? 'Mark not done' : 'Mark done');
    return box;
  }
  ignoreEvent() { return false; }
}

class ChipWidget extends WidgetType {
  constructor(readonly taskId: string, readonly label: string, readonly deleted: boolean) { super(); }
  eq(o: ChipWidget) { return o.taskId === this.taskId && o.label === this.label && o.deleted === this.deleted; }
  toDOM() {
    const chip = document.createElement('span');
    chip.className = 'cm-task-chip';
    chip.dataset.task = this.taskId;
    if (this.deleted) chip.dataset.deleted = '1';
    chip.textContent = this.label;
    chip.title = this.deleted ? 'This task is in the trash. Click to restore it.' : 'Open task';
    return chip;
  }
  ignoreEvent() { return false; }
}

function build(view: EditorView, hooks: TaskHooks) {
  const builder = new RangeSetBuilder<Decoration>();
  for (const { from, to } of view.visibleRanges) {
    for (let pos = from; pos <= to; ) {
      const line = view.state.doc.lineAt(pos);
      const m = TASK_LINE.exec(line.text);
      if (m) {
        const boxFrom = line.from + m[1].length;
        const taskId = m[6] ?? null;
        const info = taskId ? hooks.taskInfo?.(taskId) : undefined;
        const checked = info && !info.deleted ? info.status === 'done' : m[2] !== ' ';
        builder.add(boxFrom, boxFrom + 3, Decoration.replace({ widget: new CheckWidget(checked, boxFrom, taskId) }));
        const textFrom = boxFrom + 3 + m[3].length;
        const textTo = textFrom + m[4].length;
        if (textTo > textFrom && (checked || info?.deleted)) builder.add(textFrom, textTo, Decoration.mark({ class: info?.deleted ? 'cm-task-deleted' : 'cm-task-done' }));
        if (taskId && m[5]) {
          const commentFrom = textTo + (m[5].length - m[5].trimStart().length);
          const label = info?.deleted ? 'deleted · restore' : [info?.due ? 'due ' + info.due.slice(5, 10) : '', info?.priority ? 'P' + info.priority : ''].filter(Boolean).join(' · ') || 'task';
          builder.add(commentFrom, textTo + m[5].length, Decoration.replace({ widget: new ChipWidget(taskId, label, !!info?.deleted) }));
        }
      }
      pos = line.to + 1;
    }
  }
  return builder.finish();
}

/** Dispatched when task data changes (status, dates, trash) so chips and strike-through redraw. */
export const refreshTasks = StateEffect.define<null>();

export function taskLines(hooks: TaskHooks): Extension {
  const plugin = ViewPlugin.fromClass(
    class {
      decorations: DecorationSet;
      constructor(view: EditorView) { this.decorations = build(view, hooks); }
      update(u: ViewUpdate) {
        if (u.docChanged || u.viewportChanged || u.transactions.some((t) => t.reconfigured || t.effects.some((e) => e.is(refreshTasks)))) this.decorations = build(u.view, hooks);
      }
    },
    { decorations: (v) => v.decorations }
  );
  const events = EditorView.domEventHandlers({
    mousedown(event, view) {
      const target = event.target as HTMLElement;
      if (target.classList.contains('cm-task-check')) {
        event.preventDefault();
        const pos = Number(target.dataset.pos);
        const current = view.state.sliceDoc(pos, pos + 3);
        const checked = current !== '[ ]';
        const taskId = target.dataset.task;
        view.dispatch({ changes: { from: pos, to: pos + 3, insert: checked ? '[ ]' : '[x]' } });
        if (taskId) hooks.toggleTask?.(taskId, !checked);
        return true;
      }
      if (target.classList.contains('cm-task-chip') && target.dataset.task) {
        event.preventDefault();
        if (target.dataset.deleted) hooks.restoreTask?.(target.dataset.task);
        else hooks.openTask?.(target.dataset.task, event);
        return true;
      }
      return false;
    }
  });
  return [plugin, events];
}

/** Forces task decorations to refresh after task data changes. */
export function refreshDecorations(view: EditorView) {
  view.dispatch({ effects: refreshTasks.of(null) });
}
