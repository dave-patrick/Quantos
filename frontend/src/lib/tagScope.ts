/** A linked folder has no vault-owned schema directory, so its definitions stay shared. */
export function newTagScope(defaultScope: unknown, workspace: string, workspaces: string[]): string {
  return defaultScope === 'shared' || !workspaces.includes(workspace) ? '' : workspace;
}
