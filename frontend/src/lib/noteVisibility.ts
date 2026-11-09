/** Definitions are managed separately from the note browser. Tagged notes remain content. */
export const isNoteContent = (node: { type: string }) => node.type !== 'supertag' && node.type !== 'synced_block';

/** Hide only the vault-owned schema directory, preserving similarly named user folders. */
export const isNoteFolder = (path: string) => !/^workspaces\/[^/]+\/supertags(?:\/|$)/.test(path);
