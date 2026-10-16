import { api } from './api';
import { shortcutOverrides } from './commands.svelte';
import { onChange } from './events';
import { defaultToolbar, toolbar, type ToolbarItem } from './toolbar.svelte';

/** Shortcuts (Vault/shortcuts.json) and the quick-actions toolbar (Vault/toolbar.json). */
export async function loadCustomizations() {
  try {
    const s = await api.get<{ shortcuts?: Record<string, string> } | null>('config/shortcuts.json');
    shortcutOverrides.map = s?.shortcuts ?? {};
  } catch { shortcutOverrides.map = {}; }
  try {
    const t = await api.get<{ items?: ToolbarItem[] } | null>('config/toolbar.json');
    toolbar.items = t?.items?.length ? t.items : defaultToolbar;
  } catch { toolbar.items = defaultToolbar; }
}

onChange((e) => {
  const name = e.data?.config;
  if (name === 'shortcuts.json' || name === 'toolbar.json') loadCustomizations();
});

export async function saveShortcuts(map: Record<string, string>) {
  shortcutOverrides.map = map;
  await api.put('config/shortcuts.json', { shortcuts: map });
}

export async function saveToolbar(items: ToolbarItem[]) {
  toolbar.items = items;
  await api.put('config/toolbar.json', { items });
}
