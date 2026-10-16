import { api } from './api';
import { onChange } from './events';
import { isWindowsClient } from './platform';
import { app } from './state.svelte';
import { openNode } from './links.svelte';

export interface NotificationRecord { id: string; at: string; title: string; body: string; kind: string; nodeId?: string | null; read: boolean; occurrence?: string | null }

/** Notification history (BUILD-PLAN 8E.5), recorded in the vault's .state/notifications.jsonl. */
class Notifications {
  list = $state<NotificationRecord[]>([]);
  get unread() { return this.list.filter((n) => !n.read).length; }

  constructor() {
    onChange(async (e) => {
      if (e.kind !== 'Notification' || !e.data?.notification) return;
      const n = e.data.notification as NotificationRecord;
      this.list = [n, ...this.list.filter((x) => x.id !== n.id)];
      // The Windows client shows reminders natively, so a browser stays quiet while it's running.
      // Otherwise use a system notification when the page is in the background and allowed to,
      // or show it in the page so it isn't missed.
      if (isWindowsClient) return;
      const status = await api.get<{ windowsClientConnected?: boolean }>('status').catch(() => null);
      if (status?.windowsClientConnected) return;
      if ('Notification' in window && Notification.permission === 'granted' && document.hidden) {
        new Notification(n.title, { body: n.body, tag: n.id });
      } else if (n.kind === 'reminder') {
        const id = n.nodeId;
        app.toast(`Reminder: ${n.title}${n.body && n.body !== 'Reminder' ? ` · ${n.body}` : ''}`, 'info', id ? { label: 'Open', run: () => openNode(id, 'current') } : undefined);
      }
    });
  }

  async load() {
    try { this.list = await api.get<NotificationRecord[]>('notifications'); } catch { this.list = []; }
  }

  async markRead(ids?: string[]) {
    await api.post('notifications/read', { ids: ids ?? this.list.filter((n) => !n.read).map((n) => n.id) });
    this.list = this.list.map((n) => (!ids || ids.includes(n.id) ? { ...n, read: true } : n));
  }
}

export const notifications = new Notifications();
