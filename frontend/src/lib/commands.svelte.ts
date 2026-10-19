import { api } from './api';
import { isoDate } from './dates';
import { docs } from './docs.svelte';
import { layout } from './layout.svelte';
import { openNode } from './links.svelte';
import { app } from './state.svelte';
import { prefs } from './prefs.svelte';
import type { NodeDto } from './types';

export interface Command {
  id: string;
  title: string;
  group: string;
  keys?: string;            // default shortcut, e.g. "Mod+Shift+P"
  icon?: string;
  run: () => unknown;
  when?: () => boolean;
}

const registry = new Map<string, Command>();
export const commandList = $state<{ items: Command[] }>({ items: [] });

/** User overrides from Vault/shortcuts.json (Stage 6); command id -> key string or "" to unbind. */
export const shortcutOverrides = $state<{ map: Record<string, string> }>({ map: {} });

export function register(...commands: Command[]) {
  for (const c of commands) registry.set(c.id, c);
  commandList.items = [...registry.values()];
}

export function run(id: string) {
  const command = registry.get(id);
  if (command && (!command.when || command.when())) return command.run();
}

export function keysFor(id: string): string | undefined {
  const override = shortcutOverrides.map[id];
  if (override !== undefined) return override || undefined;
  return registry.get(id)?.keys;
}

export function normalizeKey(e: KeyboardEvent): string {
  const parts: string[] = [];
  if (e.ctrlKey || e.metaKey) parts.push('Mod');
  if (e.altKey) parts.push('Alt');
  if (e.shiftKey) parts.push('Shift');
  let key = e.key;
  if (key === ' ') key = 'Space';
  if (key.length === 1) key = key.toUpperCase();
  if (['Control', 'Shift', 'Alt', 'Meta'].includes(key)) return parts.join('+');
  parts.push(key);
  return parts.join('+');
}

export function displayKeys(keys?: string) {
  if (!keys) return '';
  const mac = /Mac/.test(navigator.platform);
  return keys.replace('Mod', mac ? '⌘' : 'Ctrl').replace('Alt', mac ? '⌥' : 'Alt').replace(/\+/g, mac ? '' : '+');
}

/** Keys used by two commands: shown as conflicts in shortcut settings. */
export function shortcutConflicts(): Map<string, string[]> {
  const byKey = new Map<string, string[]>();
  for (const c of registry.values()) {
    const k = keysFor(c.id);
    if (!k) continue;
    byKey.set(k, [...(byKey.get(k) ?? []), c.id]);
  }
  return new Map([...byKey].filter(([, ids]) => ids.length > 1));
}

export function handleGlobalKey(e: KeyboardEvent): boolean {
  const combo = normalizeKey(e);
  for (const c of registry.values()) {
    if (keysFor(c.id) === combo && (!c.when || c.when())) {
      e.preventDefault();
      c.run();
      return true;
    }
  }
  return false;
}

export async function newNote(where: 'current' | 'new' | 'other' = 'new', title = 'Untitled') {
  const node = await api.post<NodeDto>('nodes', { title, workspace: app.workspace });
  await openNode(node.id, where, node.title);
  return node;
}

export function openToday(where: 'current' | 'new' | 'other' = 'current') {
  return layout.open({ kind: 'daily', title: 'Daily' }, where);
}

register(
  { id: 'palette.files', title: 'Quick open', group: 'Navigate', keys: 'Mod+P', icon: 'search', run: () => { app.paletteMode = 'files'; app.paletteOpen = true; } },
  { id: 'palette.commands', title: 'Command palette', group: 'Navigate', keys: 'Mod+Shift+P', icon: 'command', run: () => { app.paletteMode = 'commands'; app.paletteOpen = true; } },
  { id: 'palette.files.k', title: 'Quick open (alternative)', group: 'Navigate', keys: 'Mod+K', icon: 'search', run: () => { app.paletteMode = 'files'; app.paletteOpen = true; } },
  { id: 'note.new', title: 'New note', group: 'Create', keys: 'Mod+N', icon: 'plus', run: () => newNote().catch((e) => app.error(e)) },
  { id: 'daily.today', title: 'Open daily stream', group: 'Navigate', keys: 'Mod+Shift+D', icon: 'calendar', run: () => openToday() },
  { id: 'search.open', title: 'Search the vault', group: 'Navigate', keys: 'Mod+Shift+F', icon: 'search', run: () => layout.open({ kind: 'search', title: 'Search' }, 'current') },
  { id: 'search.replace', title: 'Find and replace across notes', group: 'Edit', keys: 'Mod+Shift+H', icon: 'replace', run: () => layout.open({ kind: 'replace', title: 'Find and replace' }, 'current') },
  { id: 'ai.ask', title: 'Ask your notes', group: 'AI', keys: 'Mod+Shift+K', icon: 'sparkles', run: () => layout.open({ kind: 'ask', title: 'Ask your notes' }, 'current') },
  { id: 'tasks.open', title: 'Open tasks', group: 'Navigate', keys: 'Mod+Shift+T', icon: 'check-square', run: () => layout.open({ kind: 'tasks', title: 'Tasks' }) },
  { id: 'myday.open', title: 'Open My Day', group: 'Navigate', keys: 'Mod+Shift+M', icon: 'sun', run: () => layout.open({ kind: 'myday', title: 'My Day' }) },
  { id: 'timeline.open', title: 'Open timeline', group: 'Navigate', icon: 'clock', run: () => layout.open({ kind: 'timeline', title: 'Timeline' }) },
  { id: 'trash.open', title: 'Open trash', group: 'Navigate', icon: 'trash', run: () => layout.open({ kind: 'trash', title: 'Trash' }) },
  { id: 'diagnostics.open', title: 'Vault diagnostics', group: 'Vault', icon: 'activity', run: () => layout.open({ kind: 'diagnostics', title: 'Vault diagnostics' }) },
  { id: 'view.reading', title: 'Reading mode (no editing)', group: 'View', keys: 'Mod+Alt+R', icon: 'book', run: () => { prefs.reading = !prefs.reading; app.toast(prefs.reading ? 'Reading mode: notes are read-only until you turn it off (Ctrl+Alt+R).' : 'Reading mode off.', 'info'); } },
  { id: 'settings.open', title: 'Settings', group: 'App', keys: 'Mod+,', icon: 'settings', run: () => layout.open({ kind: 'settings', title: 'Settings' }) },
  { id: 'nav.back', title: 'Go back', group: 'Navigate', keys: 'Alt+ArrowLeft', icon: 'arrow-left', run: () => layout.back() },
  { id: 'nav.forward', title: 'Go forward', group: 'Navigate', keys: 'Alt+ArrowRight', icon: 'arrow-right', run: () => layout.forward() },
  { id: 'pane.split.right', title: 'Split right', group: 'Layout', keys: 'Mod+\\', icon: 'split', run: () => layout.splitPane('vertical') },
  { id: 'pane.split.down', title: 'Split down', group: 'Layout', icon: 'split-h', run: () => layout.splitPane('horizontal') },
  { id: 'pane.close', title: 'Close split', group: 'Layout', icon: 'x', run: () => layout.closePane(layout.activePane), when: () => layout.panes.length > 1 },
  { id: 'pane.focus.other', title: 'Focus other pane', group: 'Layout', keys: 'Mod+Alt+ArrowRight', run: () => { if (layout.panes.length > 1) layout.activePane = layout.activePane ? 0 : 1; } },
  { id: 'tab.close', title: 'Close tab', group: 'Layout', keys: 'Mod+W', icon: 'x', run: () => { const p = layout.pane; if (p.active) layout.close(layout.activePane, p.active); } },
  { id: 'tab.pin', title: 'Pin or unpin tab', group: 'Layout', icon: 'pin', run: () => { const p = layout.pane; if (p.active) layout.togglePin(layout.activePane, p.active); } },
  { id: 'sidebar.left', title: 'Toggle left sidebar', group: 'Layout', keys: 'Mod+[', icon: 'panel-left', run: () => (app.leftOpen = !app.leftOpen) },
  { id: 'sidebar.right', title: 'Toggle right sidebar', group: 'Layout', keys: 'Mod+]', icon: 'panel-right', run: () => (app.rightOpen = !app.rightOpen) },
  { id: 'theme.toggle', title: 'Switch light or dark theme', group: 'App', icon: 'moon', run: () => (app.theme = app.theme === 'dark' ? 'light' : 'dark') },
  { id: 'save.all', title: 'Save all', group: 'App', icon: 'download', run: () => docs.flushAll() },
  { id: 'cache.rebuild', title: 'Rebuild cache', group: 'Vault', icon: 'refresh', run: async () => { await api.post('system/rebuild'); app.toast('Cache rebuilt from the vault.', 'ok'); } },
  { id: 'daily.date', title: `Insert today's date`, group: 'Edit', run: () => navigator.clipboard?.writeText(isoDate()) }
);
