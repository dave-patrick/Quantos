// Quantos shared frontend shell (Stage 1).
// Runs in a normal browser and inside the Windows client (WebView2). It never touches files directly:
// every change goes through the backend's single write service.

import { Api, ApiError, CachedNode, NodeSnapshot, takeToken } from "./api.js";

interface ClientBridge { postMessage(message: unknown): void }
declare global { interface Window { chrome?: { webview?: ClientBridge } } }

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const isWindowsClient = !!window.chrome?.webview;
const AUTOSAVE_MS = 1500;

let api: Api;
let workspace = "default";
let current: NodeSnapshot | null = null;
let dirty = false;
let saving = false;
let autosaveTimer: number | undefined;

// ------------------------------------------------------------------ helpers

function el<K extends keyof HTMLElementTagNameMap>(tag: K, props: Partial<HTMLElementTagNameMap[K]> = {}, ...children: (Node | string)[]) {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children);
  return node;
}

function setStatus(text: string, kind: "ok" | "busy" | "warn" | "error" = "ok") {
  const s = $("save-status");
  s.textContent = text;
  s.dataset.kind = kind;
}

function setConnection(ok: boolean) {
  const c = $("connection");
  c.textContent = ok ? "Connected" : "Backend not reachable";
  c.dataset.kind = ok ? "ok" : "error";
}

function friendly(err: unknown): string {
  if (err instanceof ApiError) return err.message;
  if (err instanceof TypeError) { setConnection(false); return "Can't reach the Quantos backend. Is it still running?"; }
  return String(err);
}

function formatBytes(n: number) {
  return n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(1)} KB` : `${(n / 1048576).toFixed(1)} MB`;
}

function localDate(d = new Date()) {
  const p = (x: number) => String(x).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

// ------------------------------------------------------------------ views

async function refreshList() {
  const list = $("note-list");
  try {
    const nodes = await api.list(workspace);
    list.replaceChildren(...nodes.map(noteItem));
    if (nodes.length === 0) list.append(el("li", { className: "empty", textContent: "No notes yet." }));
    setConnection(true);
  } catch (e) {
    list.replaceChildren(el("li", { className: "empty", textContent: friendly(e) }));
  }
}

function noteItem(n: CachedNode) {
  const button = el("button", { type: "button", className: "note-link", title: n.path }, n.title || "(untitled)");
  if (current?.header.id === n.id) button.setAttribute("aria-current", "true");
  button.addEventListener("click", () => void open(n.id));
  return el("li", {}, button);
}

async function open(id: string) {
  if (!(await leaveCurrent())) return;
  try {
    show(await api.read(id));
  } catch (e) {
    setStatus(friendly(e), "error");
  }
}

function show(node: NodeSnapshot) {
  current = node;
  dirty = false;
  $("empty-state").hidden = true;
  $("editor").hidden = false;
  $<HTMLInputElement>("title").value = node.header.title;
  $<HTMLTextAreaElement>("body").value = node.body;
  $("node-path").textContent = node.path;
  $("node-meta").textContent = `Created ${node.header.created} · Modified ${node.header.modified}`;
  $("frontmatter").textContent = node.frontmatter || "(none)";
  hideConflict();
  setStatus("Saved");
  void refreshList();
  void refreshHistory();
}

async function refreshHistory() {
  const list = $("history-list");
  if (!current) { list.replaceChildren(); return; }
  const id = current.header.id;
  try {
    const entries = await api.history(id);
    list.replaceChildren(...entries.slice(0, 20).map(h => {
      const b = el("button", { type: "button", className: "link" }, h.entry.slice(0, 15).replace(/^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})/, "$1-$2-$3 $4:$5:$6 UTC"));
      b.addEventListener("click", async () => {
        const text = await api.historyEntry(id, h.entry);
        $("history-preview").textContent = text;
        $<HTMLDialogElement>("history-dialog").showModal();
      });
      return el("li", {}, b);
    }));
    if (entries.length === 0) list.append(el("li", { className: "empty", textContent: "No earlier versions yet." }));
  } catch { list.replaceChildren(); }
}

// ------------------------------------------------------------------ saving

function markDirty() {
  if (!current) return;
  dirty = true;
  setStatus("Unsaved changes", "warn");
  window.clearTimeout(autosaveTimer);
  autosaveTimer = window.setTimeout(() => void save(), AUTOSAVE_MS);
}

async function save(): Promise<boolean> {
  window.clearTimeout(autosaveTimer);
  if (!current || !dirty) return true;
  if (saving) { autosaveTimer = window.setTimeout(() => void save(), 300); return false; }
  saving = true;
  setStatus("Saving…", "busy");
  const title = $<HTMLInputElement>("title").value;
  const body = $<HTMLTextAreaElement>("body").value;
  try {
    const updated = await api.update(current.header.id, current.version, { title, body });
    current = updated;
    // Keep anything typed while the save was in flight.
    dirty = $<HTMLInputElement>("title").value !== title || $<HTMLTextAreaElement>("body").value !== body;
    $("frontmatter").textContent = updated.frontmatter;
    $("node-meta").textContent = `Created ${updated.header.created} · Modified ${updated.header.modified}`;
    setStatus(dirty ? "Unsaved changes" : "Saved", dirty ? "warn" : "ok");
    void refreshList();
    void refreshHistory();
    return true;
  } catch (e) {
    if (e instanceof ApiError && e.code === "version_conflict") showConflict();
    else setStatus(`Not saved: ${friendly(e)}`, "error");
    return false;
  } finally {
    saving = false;
  }
}

/** Saves before navigating away. Returns false if the user should stay (conflict or failed save). */
async function leaveCurrent(): Promise<boolean> {
  if (!dirty) return true;
  return save();
}

// Conflict: the file changed on disk (another window, or an edit outside the app).
// Nothing is thrown away: the user loads the disk version or keeps theirs as a separate copy.
function showConflict() {
  setStatus("Conflict: not saved", "error");
  $("conflict").hidden = false;
}
function hideConflict() { $("conflict").hidden = true; }

async function loadDiskVersion() {
  if (!current) return;
  const mine = $<HTMLTextAreaElement>("body").value;
  const title = $<HTMLInputElement>("title").value;
  const copy = await api.create(workspace, `${title} (my unsaved copy)`, mine);
  dirty = false;
  show(await api.read(current.header.id));
  setStatus(`Loaded the disk version. Your edits were kept as "${copy.header.title}".`, "warn");
}

async function keepMineAsCopy() {
  if (!current) return;
  const copy = await api.create(workspace, `${$<HTMLInputElement>("title").value} (copy)`, $<HTMLTextAreaElement>("body").value);
  dirty = false;
  show(copy);
  setStatus("Your version was saved as a new note. The original is unchanged.", "warn");
}

// ------------------------------------------------------------------ commands

async function newNote() {
  if (!(await leaveCurrent())) return;
  const title = prompt("Title for the new note:", "Untitled");
  if (!title?.trim()) return;
  try { show(await api.create(workspace, title.trim())); $<HTMLTextAreaElement>("body").focus(); }
  catch (e) { setStatus(friendly(e), "error"); }
}

async function openToday() {
  if (!(await leaveCurrent())) return;
  try { show(await api.daily(localDate(), workspace)); }
  catch (e) { setStatus(friendly(e), "error"); }
}

async function trashCurrent() {
  if (!current || !(await leaveCurrent())) return;
  if (!confirm(`Move "${current.header.title}" to the trash? You can restore it any time.`)) return;
  try {
    await api.trash(current.header.id, current.version);
    current = null;
    $("editor").hidden = true;
    $("empty-state").hidden = false;
    setStatus("Moved to trash");
    void refreshList();
  } catch (e) {
    if (e instanceof ApiError && e.code === "version_conflict") showConflict();
    else setStatus(friendly(e), "error");
  }
}

async function showTrash() {
  const list = $("trash-list");
  const entries = await api.listTrash();
  list.replaceChildren(...entries.map(t => {
    const b = el("button", { type: "button" }, "Restore");
    b.addEventListener("click", async () => {
      const result = await api.restore(t.trashId);
      $<HTMLDialogElement>("trash-dialog").close();
      show(result.node);
      if (result.renamed) setStatus(`Restored as ${result.node.path} because the original name is taken.`, "warn");
    });
    return el("li", {}, el("span", {}, `${t.title} `, el("small", {}, `(${t.originalPath}, trashed ${t.trashedAt})`)), b);
  }));
  if (entries.length === 0) list.append(el("li", { className: "empty", textContent: "The trash is empty." }));
  $<HTMLDialogElement>("trash-dialog").showModal();
}

async function showDiagnostics() {
  const s = await api.status();
  const d = s.diagnostics;
  $("diag-body").replaceChildren(
    el("p", {}, `Vault: ${s.vault}`),
    el("p", {}, `Cache: ${d.cachePath}`),
    el("p", {}, `Notes: ${Object.entries(d.nodes).map(([k, v]) => `${v} ${k}`).join(", ") || "none"}`),
    el("p", {}, `History: ${formatBytes(d.historyBytes)} · Trash: ${formatBytes(d.trashBytes)}`),
    el("h3", {}, "Problems"),
    d.problems.length
      ? el("ul", {}, ...d.problems.map(p => el("li", {}, `${p.path}: ${p.problem}`)))
      : el("p", {}, "No problems found."),
  );
  $<HTMLDialogElement>("diag-dialog").showModal();
}

async function rebuildCache() {
  setStatus("Rebuilding cache…", "busy");
  try {
    const r = await api.rebuildCache();
    setStatus(`Cache rebuilt: ${r.indexed} notes, ${r.problems} problems.`);
    void refreshList();
  } catch (e) { setStatus(friendly(e), "error"); }
}

// ------------------------------------------------------------------ startup

async function start() {
  // Features that need the Windows client are labelled in the browser; the terminal is hidden.
  document.body.dataset.client = isWindowsClient ? "windows" : "browser";
  for (const node of document.querySelectorAll<HTMLElement>("[data-requires-windows]")) {
    if (!isWindowsClient) {
      node.setAttribute("aria-disabled", "true");
      node.append(el("span", { className: "badge" }, "Requires Windows client"));
    }
  }

  const token = takeToken();
  if (!token) {
    $("no-token").hidden = false;
    $("app").hidden = true;
    return;
  }
  api = new Api(token);

  try {
    const status = await api.status();
    setConnection(true);
    $("vault-path").textContent = status.vault;
    $("app-version").textContent = `v${status.version}`;
    if (status.warnings.length) {
      $("warnings").hidden = false;
      $("warnings").replaceChildren(...status.warnings.map(w => el("p", {}, w)));
    }
  } catch (e) {
    setConnection(false);
    setStatus(friendly(e), "error");
    if (e instanceof ApiError && e.status === 401) { $("no-token").hidden = false; $("app").hidden = true; }
    return;
  }

  $("new-note").addEventListener("click", () => void newNote());
  $("open-today").addEventListener("click", () => void openToday());
  $("show-trash").addEventListener("click", () => void showTrash());
  $("show-diagnostics").addEventListener("click", () => void showDiagnostics());
  $("rebuild-cache").addEventListener("click", () => void rebuildCache());
  $("save").addEventListener("click", () => void save());
  $("trash").addEventListener("click", () => void trashCurrent());
  $("conflict-load").addEventListener("click", () => void loadDiskVersion());
  $("conflict-copy").addEventListener("click", () => void keepMineAsCopy());
  $("title").addEventListener("input", markDirty);
  $("body").addEventListener("input", markDirty);
  for (const d of document.querySelectorAll<HTMLDialogElement>("dialog")) {
    d.querySelector("[data-close]")?.addEventListener("click", () => d.close());
  }
  document.addEventListener("keydown", e => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") { e.preventDefault(); void save(); }
  });
  window.addEventListener("beforeunload", e => { if (dirty) e.preventDefault(); });
  // Exit (Windows client only): save first, then ask the client to stop the backend safely.
  $("exit-app").addEventListener("click", async () => {
    if (dirty && !(await save())) { alert("This note couldn't be saved yet. Resolve the message above before exiting."); return; }
    window.chrome?.webview?.postMessage({ type: "exit" });
  });

  await refreshList();
}

void start();
