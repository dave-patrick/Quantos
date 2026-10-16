export interface NodeDto {
  id: string;
  path: string;
  title: string;
  type: string;
  workspace: string;
  tags: string[];
  aliases: string[];
  created: string | null;
  modified: string | null;
  archived: boolean;
  readOnly: boolean;
  yamlError: string | null;
  schema: number;
  properties: Record<string, unknown>;
  body: string;
  content: string;
  hash: string;
}

export interface NodeSummary {
  id: string;
  path: string;
  title: string;
  type: string;
  workspace: string;
  tags: string[];
  created: string | null;
  modified: string | null;
  archived: boolean;
  readOnly: boolean;
  properties: Record<string, unknown>;
}

export interface AmbiguousLink { text: string; candidates: NodeSummary[] }

export interface SaveResult {
  status: 'saved' | 'unchanged' | 'conflict' | 'notfound' | 'readonly' | 'invalid';
  node: NodeDto | null;
  diskContent: string | null;
  diskHash: string | null;
  message: string | null;
  ambiguous: AmbiguousLink[] | null;
}

export interface SearchHit { id: string; title: string; type: string; workspace: string; path: string; snippet: string }

export interface LinkRef { sourceId: string; sourceTitle: string; sourceWorkspace: string; target: string; kind: string; context: string }

export interface TrashEntry {
  entryId: string; id: string; title: string; type: string; originalPath: string; fileName: string; trashedAt: string; referrers: string[];
}

export interface HistoryVersion { version: string; savedAt: string; size: number; kind: string }

export interface Diagnostic { severity: string; code: string; message: string; path: string | null; id: string | null }

export interface ReconcileReport {
  scanned: number; changed: number; added: number; removed: number; adopted: number; tempFilesRemoved: number;
  recovered: { opId: string; description: string; outcome: string; problems: string[] }[];
  diagnostics: Diagnostic[]; elapsed: string; rebuilt?: boolean;
}

export interface Status {
  version: string; pid: number; port: number; clientData: string; vault: string | null; workspaces: string[] | null;
  lastWorkspace: string | null; nodeCount: number | null; syncedFolderWarning: string | null; reconcile: ReconcileReport | null;
  safeMode: boolean; timeZone: string; utcOffset: string;
  actor: { kind: string; name: string; native: boolean; windowsClient: boolean };
}

export interface VaultCheck {
  path: string; exists: boolean; empty: boolean; looksLikeVault: boolean; writable: boolean; syncedService: string | null; warning: string | null; error: string | null;
}

export interface TreeFolder { name: string; path: string; folders: TreeFolder[]; nodes: TreeNode[] }
export interface TreeNode { id: string; title: string; type: string; path: string; modified: string | null; archived: boolean }

export type ChangeKind = 'Created' | 'Updated' | 'Moved' | 'Trashed' | 'Restored' | 'Deleted' | 'Archived' | 'Unarchived' | 'Conflict' | 'Reindexed' | 'Notification' | 'Approval';

export interface ChangeEvent { kind: ChangeKind; id: string | null; path: string | null; oldPath: string | null; hash: string | null; origin: string | null; data: any }

export interface Approval { id: string; actor: { kind: string; name: string }; permission: string; description: string; requested: string; state: string }
