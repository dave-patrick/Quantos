import type { ChangeEvent } from './types';

type Handler = (event: ChangeEvent) => void;
const handlers = new Set<Handler>();
let source: EventSource | null = null;
let retry = 1000;

export const connection = { connected: false, listeners: new Set<(connected: boolean) => void>() };

function setConnected(value: boolean) {
  connection.connected = value;
  connection.listeners.forEach((l) => l(value));
}

/** Subscribes to the backend push channel (server-sent events); reconnects with backoff. */
export function startEvents() {
  if (source) return;
  const open = () => {
    source = new EventSource('/api/events', { withCredentials: true });
    source.addEventListener('hello', () => {
      retry = 1000;
      setConnected(true);
    });
    source.addEventListener('change', (e) => {
      const event = JSON.parse((e as MessageEvent).data) as ChangeEvent;
      handlers.forEach((h) => {
        try { h(event); } catch (err) { console.error(err); }
      });
    });
    source.onerror = () => {
      setConnected(false);
      source?.close();
      source = null;
      setTimeout(open, retry);
      retry = Math.min(retry * 2, 15000);
    };
  };
  open();
}

export function onChange(handler: Handler) {
  handlers.add(handler);
  return () => handlers.delete(handler);
}
