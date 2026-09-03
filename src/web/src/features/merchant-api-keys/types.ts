export interface ApiKey {
  id: string;
  prefix: string;
  scopes: string[];
  isActive: boolean;
  createdAt: string;
  revokedAt: string | null;
  lastUsedAt: string | null;
}

// PlaintextKey appears here only — the one-time reveal, immediately after POST. Never
// reconstructable from ApiKey or any later read (mirrors ApiKeyCreatedResponse's own doc
// comment).
export interface ApiKeyCreated {
  id: string;
  plaintextKey: string;
  prefix: string;
  scopes: string[];
  createdAt: string;
}
