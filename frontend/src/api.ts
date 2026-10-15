// Typed client for the local Quantos backend (API v1).
// The access token is per launch; it arrives in the link (#token=...) and is kept in session storage only.

export interface NodeHeader {
  id: string; title: string; workspace: string; schema: number;
  created: string; modified: string; state: string; supertags: string[];
}
export interface NodeSnapshot {
  header: NodeHeader; path: string; version: string; body: string; frontmatter: string; state: string; trashId: string | null;
}
export interface CachedNode { id: string; path: string; workspace: string; title: string; state: string; modified: string; version: string; }
export interface TrashEntry { trashId: string; nodeId: string; title: string; workspace: string; originalPath: string; trashedAt: string; }
export interface HistoryEntry { entry: string; bytes: number; }
export interface Status {
  app: string; version: string; vault: string; warnings: string[]; workspaces: string[];
  diagnostics: { nodes: Record<string, number>; historyBytes: number; trashBytes: number; problems: { path: string; problem: string }[]; cachePath: string };
}

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string, public body: Record<string, unknown>) { super(message); }
}

const TOKEN_KEY = "quantos.token";

/** Reads the token from the launch link, stores it for this tab, and removes it from the address bar. */
export function takeToken(): string | null {
  const match = /(?:^#|&)token=([A-Za-z0-9_-]+)/.exec(location.hash);
  if (match?.[1]) {
    try { sessionStorage.setItem(TOKEN_KEY, match[1]); } catch { /* storage blocked: keep in memory only */ }
    history.replaceState(null, "", location.pathname + location.search);
    return match[1];
  }
  try { return sessionStorage.getItem(TOKEN_KEY); } catch { return null; }
}

export class Api {
  constructor(private token: string) {}

  private async call<T>(method: string, path: string, body?: unknown): Promise<T> {
    const res = await fetch(`/api${path}`, {
      method,
      headers: { "X-Quantos-Token": this.token, ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await res.text();
    const data = text && res.headers.get("content-type")?.includes("json") ? JSON.parse(text) : text;
    if (!res.ok) {
      const d = (typeof data === "object" && data) ? data as Record<string, unknown> : {};
      throw new ApiError(res.status, String(d.error ?? "error"), String(d.message ?? res.statusText), d);
    }
    return data as T;
  }

  status() { return this.call<Status>("GET", "/status"); }
  list(workspace: string) { return this.call<CachedNode[]>("GET", `/workspaces/${encodeURIComponent(workspace)}/nodes`); }
  read(id: string) { return this.call<NodeSnapshot>("GET", `/nodes/${encodeURIComponent(id)}`); }
  create(workspace: string, title: string, body = "") { return this.call<NodeSnapshot>("POST", "/nodes", { workspace, title, body }); }
  update(id: string, expectedVersion: string, change: { title?: string; body?: string }) {
    return this.call<NodeSnapshot>("PUT", `/nodes/${encodeURIComponent(id)}`, { expectedVersion, ...change });
  }
  trash(id: string, expectedVersion: string) { return this.call<TrashEntry>("POST", `/nodes/${encodeURIComponent(id)}/trash`, { expectedVersion }); }
  listTrash() { return this.call<TrashEntry[]>("GET", "/trash"); }
  restore(trashId: string) { return this.call<{ node: NodeSnapshot; renamed: boolean }>("POST", `/trash/${encodeURIComponent(trashId)}/restore`); }
  history(id: string) { return this.call<HistoryEntry[]>("GET", `/nodes/${encodeURIComponent(id)}/history`); }
  historyEntry(id: string, entry: string) { return this.call<string>("GET", `/nodes/${encodeURIComponent(id)}/history/${encodeURIComponent(entry)}`); }
  daily(date: string, workspace: string) { return this.call<NodeSnapshot>("GET", `/daily/${date}?workspace=${encodeURIComponent(workspace)}`); }
  rebuildCache() { return this.call<{ indexed: number; problems: number }>("POST", "/cache/rebuild"); }
}
