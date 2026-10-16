/** Quick-actions toolbar (customizable in Settings; stored in Vault/toolbar.json). */
export interface ToolbarItem { id: string; label: string; title: string; icon?: string }

export const defaultToolbar: ToolbarItem[] = [
  { id: 'note.new', label: 'New', title: 'New note', icon: 'plus' },
  { id: 'daily.today', label: 'Daily', title: 'Daily stream', icon: 'calendar' },
  { id: 'myday.open', label: 'My Day', title: 'My Day', icon: 'sun' },
  { id: 'tasks.open', label: 'Tasks', title: 'Tasks', icon: 'check-square' },
  { id: 'pane.split.right', label: 'Split', title: 'Split right', icon: 'split' }
];

class Toolbar {
  items = $state<ToolbarItem[]>(defaultToolbar);
}

export const toolbar = new Toolbar();
