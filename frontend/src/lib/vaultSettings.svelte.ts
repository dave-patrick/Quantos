import { api } from './api';

/** Vault-wide settings (Vault/settings.json). Unknown keys are kept when saving. */
class VaultSettings {
  data = $state<Record<string, unknown>>({});
  private loading: Promise<void> | null = null;

  load() {
    this.loading = (async () => {
      try { this.data = (await api.get<Record<string, unknown> | null>('config/settings.json')) ?? {}; } catch { this.data = {}; }
    })();
    return this.loading;
  }

  async set(key: string, value: unknown) {
    return this.patch({ [key]: value });
  }

  /** Sets several keys in one write. A null value removes the key. */
  async patch(values: Record<string, unknown | null>) {
    // Saving before the file has been read would write back only these keys and lose the rest.
    await (this.loading ?? this.load());
    const next = { ...this.data };
    for (const [key, value] of Object.entries(values)) {
      if (value === null || value === undefined || value === '') delete next[key]; else next[key] = value;
    }
    this.data = next;
    await api.put('config/settings.json', next);
  }
}

export const vaultSettings = new VaultSettings();
