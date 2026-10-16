import { bridge, isWindowsClient } from './platform';

/** The local PowerShell terminal lives in the Windows client; the browser never shows it. */
class Terminal {
  open = $state(false);
  height = $state(260);
  sessions = $state<{ id: string; output: string; cwd: string; running: boolean }[]>([]);
  active = $state<string | null>(null);

  constructor() {
    if (!isWindowsClient) return;
    bridge.on((msg) => {
      if (msg?.type !== 'terminal') return;
      if (msg.event === 'opened') {
        this.sessions = [...this.sessions, { id: msg.id, output: '', cwd: msg.cwd ?? '', running: false }];
        this.active = msg.id;
      } else if (msg.event === 'output') {
        const s = this.sessions.find((x) => x.id === msg.id);
        if (s) { s.output = (s.output + msg.data).slice(-200_000); s.cwd = msg.cwd ?? s.cwd; }
      } else if (msg.event === 'state') {
        const s = this.sessions.find((x) => x.id === msg.id);
        if (s) { s.running = !!msg.running; if (msg.cwd) s.cwd = msg.cwd; }
      } else if (msg.event === 'closed') {
        this.sessions = this.sessions.filter((x) => x.id !== msg.id);
        if (this.active === msg.id) this.active = this.sessions[0]?.id ?? null;
      }
    });
  }

  toggle() {
    if (!isWindowsClient) return;
    this.open = !this.open;
    if (this.open && this.sessions.length === 0) bridge.send({ type: 'terminal', action: 'open' });
  }

  newSession() { bridge.send({ type: 'terminal', action: 'open' }); }
  input(id: string, data: string) { bridge.send({ type: 'terminal', action: 'input', id, data }); }
  close(id: string) { bridge.send({ type: 'terminal', action: 'close', id }); }
}

export const terminal = new Terminal();
