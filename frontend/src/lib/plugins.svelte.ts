import { api, instanceId } from './api';
import { register } from './commands.svelte';
import { app } from './state.svelte';

export interface PluginInfo {
  name: string; version: string; description: string | null; commands: { id: string; title: string }[];
  permissions: string[]; error: string | null; enabled: boolean; granted: string[];
}

/**
 * Runs enabled plugins in sandboxed frames (no same-origin, no cookies, nonce-only scripts).
 * A plugin can only ask this page to call the API for it; each call carries the plugin's name,
 * and the backend applies that plugin's grants. Safe mode turns all of this off.
 */
class Plugins {
  list = $state<PluginInfo[]>([]);
  private frames = new Map<string, HTMLIFrameElement>();
  private started = false;

  async load() {
    try { this.list = await api.get<PluginInfo[]>('plugins'); } catch { this.list = []; }
  }

  async start() {
    if (this.started) return;
    this.started = true;
    addEventListener('message', (e) => this.onMessage(e));
    await this.load();
    if (app.status?.safeMode) return;
    for (const p of this.list.filter((x) => x.enabled && !x.error)) this.mount(p.name);
  }

  mount(name: string) {
    if (this.frames.has(name)) return;
    const frame = document.createElement('iframe');
    frame.setAttribute('sandbox', 'allow-scripts');
    frame.src = `/plugin-host/${encodeURIComponent(name)}`;
    frame.style.display = 'none';
    frame.title = `Plugin ${name}`;
    document.body.appendChild(frame);
    this.frames.set(name, frame);
  }

  unmount(name: string) {
    this.frames.get(name)?.remove();
    this.frames.delete(name);
  }

  private nameOf(source: MessageEventSource | null) {
    for (const [name, frame] of this.frames) if (frame.contentWindow === source) return name;
    return null;
  }

  private async onMessage(e: MessageEvent) {
    const name = this.nameOf(e.source);
    if (!name || !e.data?.quantos || typeof e.data.id !== 'number') return;
    const reply = (payload: object) => (e.source as Window).postMessage({ quantos: true, id: e.data.id, ...payload }, '*');
    const { method, args } = e.data;
    try {
      if (method === 'toast') { app.toast(`${name}: ${String(args?.message ?? '')}`.slice(0, 300), 'info'); reply({ result: true }); return; }
      if (method === 'command') {
        const id = `plugin.${name}.${String(args.id)}`;
        register({ id, title: `${String(args.title)} (${name})`, group: 'Plugins', icon: 'puzzle', run: () => (e.source as Window).postMessage({ quantos: true, command: String(args.id) }, '*') });
        reply({ result: true });
        return;
      }
      if (method === 'api') {
        const path = String(args?.path ?? '').replace(/^\/+/, '');
        if (!/^[\w\-/?=&%.,:]*$/.test(path) || path.startsWith('auth') || path.startsWith('system') || path.startsWith('plugins') || path.startsWith('approvals') || path.startsWith('setup')) {
          reply({ error: 'That endpoint is not available to plugins.' });
          return;
        }
        const res = await fetch('/api/' + path, {
          method: String(args?.method ?? 'GET').toUpperCase(), credentials: 'same-origin',
          headers: { 'Content-Type': 'application/json', 'X-Quantos-Request': '1', 'X-Quantos-Plugin': name, 'X-Quantos-Instance': instanceId },
          body: args?.body === undefined ? undefined : JSON.stringify(args.body)
        });
        const data = await res.json().catch(() => null);
        if (!res.ok) reply({ error: data?.error ?? res.statusText });
        else reply({ result: data });
        return;
      }
      reply({ error: 'Unknown request.' });
    } catch (err) { reply({ error: (err as Error).message }); }
  }
}

export const plugins = new Plugins();
