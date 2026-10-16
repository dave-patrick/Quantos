import { bridge, isWindowsClient } from './platform';

/** The local PowerShell terminal lives in the Windows client; the browser never shows it. */
class Terminal {
  open = $state(false);
  height = $state(260);
  denied = $state(false);
  sessions = $state<{ id: string; output: string; cwd: string; running: boolean }[]>([]);
  active = $state<string | null>(null);

  constructor() {
    if (!isWindowsClient) return;
    bridge.on((msg) => {
      if (msg?.type !== 'terminal') return;
      if (msg.event === 'denied') { this.denied = true; return; }
      if (msg.event === 'opened') {
        this.denied = false;
        this.sessions = [...this.sessions, { id: msg.id, output: '', cwd: msg.cwd ?? '', running: false }];
        this.active = msg.id;
        return;
      }
      const s = this.sessions.find((x) => x.id === msg.id);
      if (!s) return;
      if (msg.event === 'output') { s.output = (s.output + msg.data).slice(-200_000); if (msg.cwd) s.cwd = msg.cwd; }
      else if (msg.event === 'clear') s.output = '';
      else if (msg.event === 'state') { s.running = !!msg.running; if (msg.cwd) s.cwd = msg.cwd; }
      else if (msg.event === 'closed') {
        this.sessions = this.sessions.filter((x) => x.id !== msg.id);
        if (this.active === msg.id) this.active = this.sessions[0]?.id ?? null;
      }
    });
  }

  toggle() {
    if (!isWindowsClient) return;
    this.open = !this.open;
    if (this.open && this.sessions.length === 0) this.newSession();
  }

  newSession() { this.denied = false; bridge.send({ type: 'terminal', action: 'open' }); }
  input(id: string, data: string) { bridge.send({ type: 'terminal', action: 'input', id, data }); }
  stop(id: string) { bridge.send({ type: 'terminal', action: 'stop', id } as never); }
  close(id: string) { bridge.send({ type: 'terminal', action: 'close', id }); }
}

export const terminal = new Terminal();
