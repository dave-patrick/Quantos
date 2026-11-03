export interface AutomationGrantRow {
  name: string;
  permissions: string[];
  saved: boolean;
}

export function automationGrantRows(savedNames: readonly string[], grants: Record<string, string[]>): AutomationGrantRow[] {
  const saved = new Set(savedNames.filter((name) => name.trim()));
  const names = new Set([...saved, ...Object.keys(grants)]);
  return [...names].map((name) => ({
    name,
    // Match PermissionService defaults only when no explicit grant exists; [] revokes everything.
    permissions: Object.hasOwn(grants, name) ? [...grants[name]] : ['Read', 'CreateEdit'],
    saved: saved.has(name)
  }));
}
