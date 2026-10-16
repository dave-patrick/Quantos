import { api } from './api';
import { docs } from './docs.svelte';
import { layout } from './layout.svelte';
import { app } from './state.svelte';

/** Switching workspaces saves open work, then restores that workspace's layout and scroll positions. */
export async function switchWorkspace(name: string) {
  if (name === app.workspace) return;
  await docs.flushAll();
  await api.put(`workspace-state/${app.workspace}`, { layout: layout.serialize() }).catch(() => {});
  app.workspace = name;
  await api.post('system/workspace', { name }).catch(() => {});
  await layout.load(name);
  if (layout.panes.every((p) => p.tabs.length === 0)) await layout.open({ kind: 'daily', title: 'Daily' });
}
