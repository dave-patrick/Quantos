import { tick } from 'svelte';
import { docs } from './docs.svelte';
import { tasks } from './tasks.svelte';
import { app } from './state.svelte';
import { writeGate } from './writeGate';

export const taskPopup = $state({ id: null as string | null, busy: false, error: '' });
let checkpoint = 0;
let returnTo: (() => void) | null = null;

function snapshot(origin: HTMLElement | null) {
  const editor = origin?.closest('.cm-editor')?.querySelector<HTMLElement>('.cm-content');
  const opener = editor ?? origin;
  const elements = new Set<HTMLElement>(document.querySelectorAll<HTMLElement>('.scroll'));
  for (let element = origin; element; element = element.parentElement) elements.add(element);
  const positions = [...elements].map((element) => ({ element, top: element.scrollTop, left: element.scrollLeft }));
  return () => {
    if (opener?.isConnected) opener.focus({ preventScroll: true });
    for (const { element, top, left } of positions) if (element.isConnected) {
      element.scrollTop = top; element.scrollLeft = left;
    }
  };
}
function checkDocument(id: string | null | undefined) {
  const doc = id ? docs.peek(id) : null;
  if (doc && (doc.dirty || doc.conflict || doc.node && doc.status === 'error'))
    throw new Error('Save or resolve the editor’s changes before continuing.');
}
async function save() {
  if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
  await tick();
  await docs.flushAll();
  await writeGate.wait();
  checkDocument(taskPopup.id);
  if (writeGate.failedSince(checkpoint)) throw new Error('A change could not be saved. Retry the failed change before closing.');
}
function failed(error: unknown) {
  taskPopup.error = error instanceof Error ? error.message : String(error);
  app.error(error);
}
export async function openTaskPopup(id: string, origin: HTMLElement | null = document.activeElement instanceof HTMLElement ? document.activeElement : null) {
  if (taskPopup.busy || taskPopup.id === id) return false;
  const first = !taskPopup.id;
  const restore = first ? snapshot(origin) : null;
  const source = first ? origin?.closest<HTMLElement>('[data-node-id]')?.dataset.nodeId ?? tasks.get(id)?.source : null;
  if (first) checkpoint = writeGate.checkpoint();
  taskPopup.busy = true;
  taskPopup.error = '';
  try {
    await save();
    checkDocument(source);
    if (first) returnTo = restore;
    taskPopup.id = id;
    return true;
  } catch (error) { failed(error); return false; }
  finally { taskPopup.busy = false; }
}
export async function closeTaskPopup() {
  if (taskPopup.busy) return false;
  if (!taskPopup.id) return true;
  taskPopup.busy = true;
  taskPopup.error = '';
  try {
    await save();
    // Refreshing lists is optional once writes are saved; going offline must not trap the dialog.
    await tasks.load().catch((error) => app.error(error));
    await docs.refreshOpen();
    checkDocument(taskPopup.id);
    const restore = returnTo;
    taskPopup.id = null;
    returnTo = null;
    await tick();
    taskPopup.busy = false;
    await tick();
    restore?.();
    requestAnimationFrame(() => { if (!taskPopup.id && !taskPopup.busy) restore?.(); });
    return true;
  } catch (error) { failed(error); return false; }
  finally { taskPopup.busy = false; }
}
