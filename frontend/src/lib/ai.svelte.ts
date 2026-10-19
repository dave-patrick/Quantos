import { api, instanceId, ApiError } from './api';

/** Release 1.1 AI: providers and settings, streamed answers and rewrites, meaning search. */

export interface AiPreset {
  id: string; name: string; api: 'Anthropic' | 'OpenAiCompatible' | 'AzureOpenAi' | number; baseUrl: string;
  local: boolean; needsKey: boolean; chat: boolean; embeddings: boolean;
  defaultChatModel: string | null; defaultEmbeddingModel: string | null; note: string;
}
export interface AiEndpoint { provider: string; baseUrl: string | null; model: string | null }
export interface AiSettings { enabled: boolean; chat: AiEndpoint | null; embeddings: AiEndpoint | null }
export interface IndexStatus { enabled: boolean; model: string | null; notes: number; indexed: number; pending: number; working: boolean; error: string | null }
export interface AiSource { id: string; title: string; workspace: string }
export interface SemanticHit { id: string; title: string; workspace: string; path: string; text: string; score: number }

class Ai {
  presets = $state<AiPreset[]>([]);
  settings = $state<AiSettings>({ enabled: false, chat: null, embeddings: null });
  keys = $state<string[]>([]);
  index = $state<IndexStatus | null>(null);
  loaded = $state(false);

  get chatReady() { return this.settings.enabled && !!this.settings.chat?.model; }
  get searchReady() { return this.settings.enabled && !!this.settings.embeddings?.model; }
  preset(id: string | undefined) { return this.presets.find((p) => p.id === id); }

  async load() {
    try {
      const [presets, s] = await Promise.all([
        this.presets.length ? Promise.resolve(this.presets) : api.get<AiPreset[]>('ai/providers'),
        api.get<{ settings: AiSettings; keys: string[]; index: IndexStatus }>('ai/settings')
      ]);
      this.presets = presets;
      this.settings = s.settings;
      this.keys = s.keys;
      this.index = s.index;
      this.loaded = true;
    } catch { /* no vault open yet */ }
  }

  async save(next: AiSettings) {
    await api.put('ai/settings', next);
    await this.load();
  }

  async setKey(provider: string, key: string | null) {
    await api.put('ai/key', { provider, key });
    await this.load();
  }

  listModels(provider: string, baseUrl: string | null) {
    return api.post<string[]>('ai/models', { provider, baseUrl });
  }

  search(q: string, workspace?: string) {
    return api.get<SemanticHit[]>(`ai/search?q=${encodeURIComponent(q)}${workspace ? `&workspace=${encodeURIComponent(workspace)}` : ''}`);
  }
}

export const ai = new Ai();

/**
 * Reads a server-sent-event stream from a POST. Calls onEvent(name, data) for each event and
 * resolves when the stream says "done"; rejects with the provider's reason on "error".
 */
export async function streamPost(path: string, body: unknown, onEvent: (name: string, data: any) => void, signal?: AbortSignal) {
  const response = await fetch('/api/' + path, {
    method: 'POST', credentials: 'same-origin', signal,
    headers: { 'Content-Type': 'application/json', 'X-Quantos-Request': '1', 'X-Quantos-Instance': instanceId },
    body: JSON.stringify(body)
  });
  if (!response.ok || !response.body) {
    const data = await response.json().catch(() => null);
    throw new ApiError(response.status, data?.error ?? response.statusText, data);
  }
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += value;
    let end;
    while ((end = buffer.indexOf('\n\n')) >= 0) {
      const block = buffer.slice(0, end);
      buffer = buffer.slice(end + 2);
      const name = /^event: (.*)$/m.exec(block)?.[1] ?? 'message';
      const raw = /^data: (.*)$/m.exec(block)?.[1];
      const data = raw ? JSON.parse(raw) : null;
      if (name === 'error') throw new Error(data?.error ?? 'The AI request failed.');
      if (name === 'done') return;
      onEvent(name, data);
    }
  }
}

/** A rewrite in progress or finished, shown in the AI result dialog until the person chooses what to do with it. */
class AiJob {
  open = $state(false);
  title = $state('');
  text = $state('');
  working = $state(false);
  error = $state('');
  /** What Replace and Insert do; null when the result can only be copied. */
  replace: ((text: string) => void) | null = null;
  insertBelow: ((text: string) => void) | null = null;
  private controller: AbortController | null = null;

  async run(title: string, instruction: string, text: string, noteTitle: string | null, actions: { replace?: (t: string) => void; insertBelow?: (t: string) => void }) {
    this.stop();
    Object.assign(this, { open: true, title, text: '', working: true, error: '' });
    this.replace = actions.replace ?? null;
    this.insertBelow = actions.insertBelow ?? null;
    this.controller = new AbortController();
    try {
      await streamPost('ai/transform', { instruction, text, title: noteTitle }, (name, data) => { if (name === 'text') this.text += data; }, this.controller.signal);
    } catch (e) {
      if ((e as Error).name !== 'AbortError') this.error = (e as Error).message;
    } finally { this.working = false; this.controller = null; }
  }

  stop() { this.controller?.abort(); }
  close() { this.stop(); this.open = false; }
}

export const aiJob = new AiJob();
