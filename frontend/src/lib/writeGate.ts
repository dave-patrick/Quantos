/** Observe submitted writes without changing their result or error handling. */
export class WriteGate {
  private pending = new Set<Promise<unknown>>();
  private failures = new Map<string, { failure: number; operation: number }>();
  private successes = new Map<string, number>();
  private operation = 0;
  private failure = 0;
  checkpoint() { return this.failure; }
  failedSince(checkpoint: number) { return [...this.failures.values()].some((f) => f.failure > checkpoint); }
  dismiss(key: string) { this.failures.delete(key); }
  run<T>(key: string, work: () => Promise<T>): Promise<T> {
    const operation = ++this.operation;
    const promise = Promise.resolve().then(work).then((value) => {
      this.successes.set(key, Math.max(operation, this.successes.get(key) ?? 0));
      if ((this.failures.get(key)?.operation ?? 0) <= operation) this.failures.delete(key);
      return value;
    }, (error) => {
      const failure = ++this.failure;
      if ((this.successes.get(key) ?? 0) < operation && (this.failures.get(key)?.operation ?? 0) <= operation)
        this.failures.set(key, { failure, operation });
      throw error;
    });
    this.pending.add(promise);
    const done = () => { this.pending.delete(promise); };
    void promise.then(done, done);
    return promise;
  }
  async wait() { while (this.pending.size) await Promise.allSettled([...this.pending]); }
}

/** Field names distinguish retries from unrelated edits. Values never enter the key. */
export function writeKey(method: string, path: string, body?: unknown) {
  const data = body && typeof body === 'object' ? body as Record<string, unknown> : {};
  const fields = Object.keys(data).filter((k) => k !== 'baseHash').flatMap((key) => {
    if (key === 'set' && data.set && typeof data.set === 'object') return Object.keys(data.set).map((k) => 'field:' + k);
    if (key === 'remove' && Array.isArray(data.remove)) return data.remove.map((k) => 'field:' + k);
    return [key];
  }).sort();
  return `${method}:${path}:${JSON.stringify(fields)}`;
}
export const writeGate = new WriteGate();
