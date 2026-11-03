export interface SyncedBlockChoice { id: string; title: string; content: string; uses: number }

export const syncedBlockPicker = $state<{ request: { blocks: SyncedBlockChoice[]; done: (block: SyncedBlockChoice | null) => void } | null }>({ request: null });

export function pickSyncedBlock(blocks: SyncedBlockChoice[]): Promise<SyncedBlockChoice | null> {
  syncedBlockPicker.request?.done(null);
  return new Promise(resolve => {
    syncedBlockPicker.request = { blocks, done: block => { syncedBlockPicker.request = null; resolve(block); } };
  });
}

export function matchingBlocks(blocks: SyncedBlockChoice[], query: string): SyncedBlockChoice[] {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return blocks.filter(block => words.every(word => `${block.title}\n${block.content}`.toLowerCase().includes(word)));
}
