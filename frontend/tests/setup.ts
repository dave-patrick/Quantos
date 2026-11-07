// Node 25 and later define their own localStorage and sessionStorage globals, which are undefined
// without --localstorage-file and hide the page storage happy-dom would provide. Give tests an
// in-memory storage instead, so they behave the same on every Node version.
class MemoryStorage {
  private items = new Map<string, string>();
  get length() { return this.items.size; }
  key(index: number) { return [...this.items.keys()][index] ?? null; }
  getItem(key: string) { return this.items.has(key) ? this.items.get(key)! : null; }
  setItem(key: string, value: string) { this.items.set(String(key), String(value)); }
  removeItem(key: string) { this.items.delete(key); }
  clear() { this.items.clear(); }
}
for (const key of ['localStorage', 'sessionStorage'] as const) {
  if (typeof (globalThis as Record<string, unknown>).document !== 'undefined' && !(globalThis as Record<string, unknown>)[key])
    Object.defineProperty(globalThis, key, { value: new MemoryStorage(), configurable: true, writable: true });
}
