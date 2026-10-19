import { api } from './api';
import { app } from './state.svelte';

/**
 * Focus timer (Release 1.1). The running timer is kept in this browser (so it survives a reload and
 * shows in every window); a finished focus session is logged by the backend to the daily note and the task.
 */
export interface Running {
  phase: 'focus' | 'break';
  start: number;          // ms since epoch
  minutes: number;        // planned length
  taskId: string | null;
  title: string;          // task title or what the session is for
  workspace: string;
}

const KEY = 'quantos.focus';
export const LENGTHS = [15, 25, 50, 90];

class Focus {
  running = $state<Running | null>(null);
  now = $state(Date.now());
  lastLength = $state(25);
  breakLength = $state(5);
  /** Bumped after a session is logged so the Focus view reloads its totals. */
  logged = $state(0);
  private timer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    if (typeof window === 'undefined') return;
    this.read();
    try { const p = JSON.parse(localStorage.getItem(KEY + '.prefs') ?? '{}'); if (p.lastLength) this.lastLength = p.lastLength; if (p.breakLength) this.breakLength = p.breakLength; } catch { /* ignore */ }
    addEventListener('storage', (e) => { if (e.key === KEY) this.read(); });
    this.timer = setInterval(() => this.tick(), 1000);
  }

  get remaining() { return this.running ? Math.max(0, this.running.start + this.running.minutes * 60_000 - this.now) : 0; }
  get elapsedMinutes() { return this.running ? Math.floor((this.now - this.running.start) / 60_000) : 0; }

  start(minutes: number, task: { id: string; title: string; workspace?: string } | null, label = '') {
    this.lastLength = minutes;
    this.savePrefs();
    this.write({ phase: 'focus', start: Date.now(), minutes, taskId: task?.id ?? null, title: task?.title ?? label.trim(), workspace: task?.workspace ?? app.workspace });
  }

  startBreak(minutes = this.breakLength) {
    this.breakLength = minutes;
    this.savePrefs();
    this.write({ phase: 'break', start: Date.now(), minutes, taskId: null, title: 'Break', workspace: app.workspace });
  }

  /** Ends early. A focus session of a minute or more is still logged. */
  async stop() {
    const r = this.running;
    if (!r) return;
    this.write(null);
    if (r.phase === 'focus' && Date.now() - r.start >= 60_000) await this.log(r, Date.now(), false);
  }

  private async tick() {
    this.now = Date.now();
    const r = this.running;
    // The window title shows the time left, so it's visible from the taskbar.
    const title = r ? `${clock(this.remaining)} ${r.phase === 'break' ? 'break' : 'focus'} · Quantos` : 'Quantos';
    if (document.title !== title) document.title = title;
    if (!r || this.remaining > 0) return;
    // Only one window logs: whichever clears the stored timer first.
    const stored = this.stored();
    if (!stored || stored.start !== r.start) { this.running = stored; return; }
    this.write(null);
    if (r.phase === 'focus') await this.log(r, r.start + r.minutes * 60_000, true);
    else {
      app.toast('Break over. Ready for another?', 'info', { label: `Focus ${this.lastLength} min`, run: () => this.start(this.lastLength, r.taskId ? { id: r.taskId, title: r.title } : null) });
      this.chime();
    }
  }

  private async log(r: Running, end: number, finished: boolean) {
    try {
      await api.post('focus', { workspace: r.workspace, start: new Date(r.start).toISOString(), end: new Date(end).toISOString(), taskId: r.taskId, label: r.taskId ? null : r.title || null });
      this.logged++;
      const mins = Math.round((end - r.start) / 60_000);
      if (finished) {
        this.chime();
        app.toast(`Focus done: ${mins} min${r.title ? ` on ${r.title}` : ''}. Logged in today's note.`, 'ok', { label: `Break ${this.breakLength} min`, run: () => this.startBreak() });
      } else app.toast(`Logged ${mins} min${r.title ? ` on ${r.title}` : ''}.`, 'ok');
    } catch (e) { app.error(e); }
  }

  /** A short, quiet tone when a session or break ends. */
  private chime() {
    try {
      const ctx = new AudioContext();
      const o = ctx.createOscillator(), g = ctx.createGain();
      o.frequency.value = 660; g.gain.setValueAtTime(0.08, ctx.currentTime); g.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.9);
      o.connect(g).connect(ctx.destination); o.start(); o.stop(ctx.currentTime + 0.9);
    } catch { /* no audio */ }
  }

  private stored(): Running | null { try { return JSON.parse(localStorage.getItem(KEY) ?? 'null'); } catch { return null; } }
  private read() { this.running = this.stored(); this.now = Date.now(); }
  private write(r: Running | null) {
    this.running = r;
    this.now = Date.now();
    try { if (r) localStorage.setItem(KEY, JSON.stringify(r)); else localStorage.removeItem(KEY); } catch { /* ignore */ }
  }
  private savePrefs() { try { localStorage.setItem(KEY + '.prefs', JSON.stringify({ lastLength: this.lastLength, breakLength: this.breakLength })); } catch { /* ignore */ } }
}

export const focus = new Focus();

export function clock(ms: number) {
  const s = Math.ceil(ms / 1000);
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  return (h ? `${h}:${String(m).padStart(2, '0')}` : String(m)) + ':' + String(sec).padStart(2, '0');
}

export function hours(minutes: number) {
  return minutes >= 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60 ? `${minutes % 60} min` : ''}`.trim() : `${minutes} min`;
}
