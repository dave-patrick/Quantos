/** Quick-actions toolbar (customizable in Settings; stored in Vault/toolbar.json). */
export interface ToolbarItem { id: string; label: string; title: string; icon?: string }

export const hiddenToolbarActionIds = new Set(['daily.today', 'myday.open', 'tasks.open']);
export const visibleToolbarItems = (items: ToolbarItem[]) => items.filter((item) => !hiddenToolbarActionIds.has(item.id));

export const defaultToolbar: ToolbarItem[] = [
  { id: 'note.new', label: 'New', title: 'New note', icon: 'plus' },
  { id: 'pane.split.right', label: 'Split', title: 'Split right', icon: 'split' }
];

class Toolbar {
  items = $state<ToolbarItem[]>(defaultToolbar);
  showLabels = $state(false);
}

export const toolbar = new Toolbar();
