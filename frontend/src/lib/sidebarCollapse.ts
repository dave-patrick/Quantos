export type SidebarCollapsed = Record<string, boolean>;
export function normalizeSidebarCollapsed(value: unknown): SidebarCollapsed {
  const defaults: SidebarCollapsed = { planning: true, explore: true };
  if (!value || typeof value !== 'object' || Array.isArray(value)) return defaults;
  for (const [key, state] of Object.entries(value)) if (typeof state === 'boolean') defaults[key] = state;
  return defaults;
}
export function toggleSidebarCollapsed(value: SidebarCollapsed, key: string): SidebarCollapsed {
  return { ...value, [key]: !value[key] };
}
