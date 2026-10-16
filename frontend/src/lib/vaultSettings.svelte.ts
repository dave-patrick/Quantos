import { api } from './api';

/** Vault-wide settings (Vault/settings.json). Unknown keys are kept when saving. */
class VaultSettings {
  data = $state<Record<string, unknown>>({});
  async load() {
    try { this.data = (await api.get<Record<string, unknown> | null>('config/settings.json')) ?? {}; } catch { this.data = {}; }
  }
  async set(key: string, value: unknown) {
    const next = { ...this.data };
    if (value === null || value === undefined || value === '') delete next[key]; else next[key] = value;
    this.data = next;
    await api.put('config/settings.json', next);
  }
}

export const vaultSettings = new VaultSettings();
