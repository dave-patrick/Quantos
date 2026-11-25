// One source for node/workspace colours so Graph and Map agree. Values are tokens (styles/tokens.css).
export const workspacePalette = [1, 2, 3, 4, 5, 6, 7, 8].map((i) => `var(--cat-${i})`);

export const nodeTypeColors: Record<string, string> = {
  task: 'var(--warn)',
  daily: 'var(--text-muted)',
  person: 'var(--ok)',
  project: 'var(--cat-4)',
  supertag: 'var(--cat-3)',
  synced_block: 'var(--ok)',
  event: 'var(--cat-6)',
};
