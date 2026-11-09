import { bridge } from './platform';

export interface FolderPickResult { path: string | null; error?: string }

/** One native request; disposing it ignores late replies without changing the next dialog. */
export function startFolderPick(path: string, done: (result: FolderPickResult) => void): () => void {
  const requestId = crypto.randomUUID();
  let active = true;
  const off = bridge.on((message) => {
    if (!active || message?.type !== 'folder-picked' || message.requestId !== requestId) return;
    if (message.path !== null && typeof message.path !== 'string') return;
    if (message.error != null && typeof message.error !== 'string') return;
    active = false;
    off();
    done({ path: message.path, error: message.error ?? undefined });
  });
  try { bridge.send({ type: 'pick-folder', requestId, path }); }
  catch (error) {
    active = false;
    off();
    done({ path: null, error: error instanceof Error ? error.message : 'Could not open the folder picker.' });
  }
  return () => { active = false; off(); };
}
