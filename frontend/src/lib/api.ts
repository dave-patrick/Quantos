import type { Approval } from './types';
import { writeGate, writeKey } from './writeGate';

/** Identifies this browser tab, so it can ignore change events caused by its own writes. */
export const instanceId = crypto.randomUUID();

export class ApiError extends Error {
  constructor(public status: number, message: string, public body: any) {
    super(message);
  }
  get approval(): Approval | undefined {
    return this.status === 428 ? this.body?.approval : undefined;
  }
}

type Listener = (error: ApiError) => void;
const unauthorizedListeners = new Set<Listener>();
export function onUnauthorized(listener: Listener) {
  unauthorizedListeners.add(listener);
  return () => unauthorizedListeners.delete(listener);
}

async function sendRequest<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch('/api/' + path.replace(/^\//, ''), {
    method,
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'X-Quantos-Request': '1',
      'X-Quantos-Instance': instanceId
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  if (response.status === 204) return undefined as T;
  const data = await response.json().catch(() => null);
  if (!response.ok) {
    const error = new ApiError(response.status, data?.error ?? data?.message ?? response.statusText, data);
    if (response.status === 401) unauthorizedListeners.forEach((l) => l(error));
    throw error;
  }
  return data as T;
}

function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  if (method === 'GET') return sendRequest<T>(method, path, body);
  return writeGate.run(writeKey(method, path, body), () => sendRequest<T>(method, path, body));
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
  del: <T>(path: string) => request<T>('DELETE', path)
};

export const q = (params: Record<string, string | number | boolean | null | undefined>) => {
  const parts = Object.entries(params)
    .filter(([, v]) => v !== undefined && v !== null && v !== '')
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`);
  return parts.length ? '?' + parts.join('&') : '';
};
