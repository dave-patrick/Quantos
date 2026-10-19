import { api } from './api';
import { app } from './state.svelte';
import { bridge, isWindowsClient } from './platform';

/** Updates: what the backend knows (it only asks GitHub when told to, or daily if that's on). */
export interface Release { version: string; name: string; notes: string; page: string; published: string; size: number }
export interface UpdateStatus {
  current: string; latest: Release | null; available: boolean; lastCheck: string | null;
  state: 'idle' | 'checking' | 'downloading' | 'ready'; progress: number; ready: string | null; error: string | null;
  settings: { autoCheck: boolean };
}

class Updates {
  status = $state<UpdateStatus | null>(null);
  private polling: ReturnType<typeof setInterval> | undefined;

  async load() {
    try { this.status = await api.get<UpdateStatus>('updates'); } catch { /* older backend */ }
    if (this.status?.state === 'downloading') this.poll();
  }

  async check() {
    try { this.status = await api.post<UpdateStatus>('updates/check'); }
    catch (e) { app.error(e); await this.load(); }
  }

  async download() {
    try { this.status = await api.post<UpdateStatus>('updates/download'); this.poll(); }
    catch (e) { app.error(e); }
  }

  private poll() {
    clearInterval(this.polling);
    this.polling = setInterval(async () => {
      await this.load();
      if (this.status?.state !== 'downloading') {
        clearInterval(this.polling);
        if (this.status?.ready) app.toast(`Quantos ${this.status.ready} is downloaded and checked.`, 'ok', isWindowsClient ? { label: 'Install now', run: () => this.install() } : undefined);
        else if (this.status?.error) app.toast(this.status.error, 'error');
      }
    }, 600);
  }

  /** The Windows client asks to confirm, saves, swaps the program files and restarts. */
  install() { bridge.send({ type: 'update-apply' }); }

  async setAutoCheck(on: boolean) {
    try { this.status = await api.put<UpdateStatus>('updates/settings', { autoCheck: on }); } catch (e) { app.error(e); }
  }
}

export const updates = new Updates();

// The client explains when it couldn't install; and after a restart onto a new version, say so once.
bridge.on((m) => { if (m?.type === 'update-error') app.toast(m.message, 'error'); });
export async function announceVersion() {
  await updates.load();
  const v = updates.status?.current;
  if (!v) return;
  try {
    const before = localStorage.getItem('quantos.version');
    localStorage.setItem('quantos.version', v);
    if (before && before !== v) app.toast(`Quantos is now on version ${v}.`, 'ok');
  } catch { /* no storage */ }
}
