import type { ChangeEvent } from './types';

type Handler = (event: ChangeEvent) => void;
type Relay = { kind: 'change'; event: ChangeEvent } | { kind: 'state'; connected: boolean } | { kind: 'ask' };

const handlers = new Set<Handler>();
const resyncHandlers = new Set<() => void>();
let lastSeq = 0;
let hadConnection = false;   // connected at least once
let missedSome = false;      // disconnected since then
let source: EventSource | null = null;
let started = false;
let leading = false;
let relay: BroadcastChannel | null = null;
let retry = 1000;
let lastHeard = 0;

export const connection = { connected: false, listeners: new Set<(connected: boolean) => void>() };

function resync() {
  resyncHandlers.forEach((h) => { try { h(); } catch (err) { console.error(err); } });
}

function setConnected(value: boolean) {
  if (!value && hadConnection) missedSome = true;
  if (value && !connection.connected && missedSome) {
    // Back after a break: events may have been missed, so everything that shows data reloads.
    missedSome = false;
    lastSeq = 0;
    queueMicrotask(resync);
  }
  if (value) hadConnection = true;
  connection.connected = value;
  connection.listeners.forEach((l) => l(value));
  if (leading) relay?.postMessage({ kind: 'state', connected: value } satisfies Relay);
}

function dispatch(event: ChangeEvent) {
  // Events are numbered; a gap (the stream dropped some) or a restart (numbers start again) means
  // this page's data may be stale, so it reloads instead of trusting the next patch.
  if (typeof event.seq === 'number' && event.seq > 0) {
    const gap = lastSeq > 0 && event.seq !== lastSeq + 1;
    lastSeq = event.seq;
    if (gap) queueMicrotask(resync);
  }
  handlers.forEach((h) => {
    try { h(event); } catch (err) { console.error(err); }
  });
}

/** Opens this page's own push channel (server-sent events) and reconnects with backoff. */
function openSource() {
  source = new EventSource('/api/events', { withCredentials: true });
  source.addEventListener('hello', () => {
    retry = 1000;
    setConnected(true);
  });
  source.addEventListener('change', (e) => {
    const event = JSON.parse((e as MessageEvent).data) as ChangeEvent;
    dispatch(event);
    if (leading) relay?.postMessage({ kind: 'change', event } satisfies Relay);
  });
  source.onerror = () => {
    setConnected(false);
    source?.close();
    source = null;
    setTimeout(openSource, retry);
    retry = Math.min(retry * 2, 15000);
  };
}

/**
 * Subscribes to backend changes. Browsers allow only six connections per host, so the pages of
 * this origin share one stream: the page holding the lock opens it and relays events to the rest,
 * announcing itself every few seconds. When that page closes, the lock passes to another page. A
 * page that stops hearing from a leader (a frozen or sleeping tab keeps its lock) opens its own.
 */
export function startEvents() {
  if (started) return;
  started = true;
  if (typeof BroadcastChannel === 'undefined' || !navigator.locks) { openSource(); return; }
  relay = new BroadcastChannel('quantos-events');
  relay.onmessage = (m: MessageEvent<Relay>) => {
    const msg = m.data;
    if (msg.kind === 'ask') { if (leading) relay?.postMessage({ kind: 'state', connected: connection.connected } satisfies Relay); return; }
    if (leading) return;
    lastHeard = Date.now();
    if (source) return;  // this page has its own stream already
    if (msg.kind === 'change') dispatch(msg.event);
    else setConnected(msg.connected);
  };
  relay.postMessage({ kind: 'ask' } satisfies Relay);
  lastHeard = Date.now() - 9000;  // no answer within ~3 s: no live leader
  setInterval(() => {
    if (leading) relay?.postMessage({ kind: 'state', connected: connection.connected } satisfies Relay);
    else if (!source && Date.now() - lastHeard > 12000) openSource();
  }, 2500);
  navigator.locks.request('quantos-events', () => {
    leading = true;
    if (!source) openSource();
    return new Promise<never>(() => {});  // held until this page closes
  });
}

/** Called when this page may have missed events (a reconnect or a gap); reload what you show. */
export function onResync(handler: () => void) {
  resyncHandlers.add(handler);
  return () => resyncHandlers.delete(handler);
}

/** True for events about notes, tasks and files; false for notifications, approvals and shell jobs. */
export function isContentChange(event: ChangeEvent) {
  return !['Notification', 'Approval', 'ShellJob'].includes(event.kind);
}

export function onChange(handler: Handler) {
  handlers.add(handler);
  return () => handlers.delete(handler);
}
