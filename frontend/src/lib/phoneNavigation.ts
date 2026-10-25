import type { TabKind } from './layout.svelte';

type PhoneDestination = TabKind | 'capture';
export const phoneDestinations: { kind: PhoneDestination; label: string; icon: string }[] = [
  { kind: 'notes', label: 'Notes', icon: 'file-text' },
  { kind: 'tasks', label: 'Tasks', icon: 'check-square' },
  { kind: 'myday', label: 'Today', icon: 'sun' },
  { kind: 'daily', label: 'Daily notes', icon: 'calendar' },
  { kind: 'capture', label: 'Capture', icon: 'inbox' },
  { kind: 'search', label: 'Search', icon: 'search' },
  { kind: 'focus', label: 'Focus', icon: 'timer' },
  { kind: 'timeline', label: 'Timeline', icon: 'calendar' },
  { kind: 'habits', label: 'Habits', icon: 'check-square' },
  { kind: 'goals', label: 'Goals', icon: 'target' },
  { kind: 'sprints', label: 'Sprints', icon: 'zap' },
  { kind: 'gantt', label: 'Gantt', icon: 'list' },
  { kind: 'review', label: 'Stale tasks', icon: 'history' },
  { kind: 'ask', label: 'Ask your notes', icon: 'sparkles' },
  { kind: 'graph', label: 'Graph', icon: 'graph' },
  { kind: 'map', label: 'Map', icon: 'map' },
  { kind: 'replace', label: 'Find and replace', icon: 'replace' },
  { kind: 'templates', label: 'Templates', icon: 'copy' },
  { kind: 'supertags', label: 'Supertags', icon: 'tag' },
  { kind: 'dictionary', label: 'Metadata', icon: 'book' },
  { kind: 'attachments', label: 'Attachments', icon: 'paperclip' },
  { kind: 'notifications', label: 'Notifications', icon: 'bell' },
  { kind: 'trash', label: 'Trash', icon: 'trash' },
  { kind: 'diagnostics', label: 'Diagnostics', icon: 'activity' },
  { kind: 'settings', label: 'Settings', icon: 'settings' },
  { kind: 'help', label: 'Help', icon: 'help-circle' },
  { kind: 'about', label: 'About', icon: 'info' }
];
export const defaultPhoneNavigation = ['daily', 'tasks', 'capture', 'myday'];

/** Device preferences may outlive a version or contain malformed values. */
export function normalizePhoneNavigation(value: unknown): string[] {
  const allowed = new Set(phoneDestinations.map(d => d.kind));
  const items = Array.isArray(value) ? [...new Set(value.filter((v): v is PhoneDestination => typeof v === 'string' && allowed.has(v as PhoneDestination)))].slice(0, 4) : [];
  return items.length ? items : [...defaultPhoneNavigation];
}
