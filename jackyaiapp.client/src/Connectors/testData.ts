import type {
  CatalogEntry,
  ConnectorCatalog,
  InstalledConnector,
  McpProbeResult,
} from '@/apis/connectorsApis/types';

/** Test data for the connectors page; each test overrides only what it is about. */
export const entry = (patch: Partial<CatalogEntry> = {}): CatalogEntry => ({
  key: 'deepwiki',
  displayName: 'DeepWiki',
  description: '問任何 GitHub repo 的問題',
  category: 'dev',
  publisher: 'Cognition',
  authKind: 'none',
  docsUrl: null,
  installed: false,
  ...patch,
});

export const installed = (patch: Partial<InstalledConnector> = {}): InstalledConnector => ({
  id: 'i-1',
  definitionKey: 'deepwiki',
  displayName: 'DeepWiki',
  description: '問任何 GitHub repo 的問題',
  category: 'dev',
  serverUrl: 'https://mcp.deepwiki.com/mcp',
  authKind: 'none',
  health: 'ok',
  alwaysOn: false,
  lastError: null,
  lastTestedAt: '2026-10-24T00:00:00Z',
  scopes: [],
  tools: [
    {
      name: 'ask_question',
      title: '問問題',
      description: '問一個 repo 的問題',
      effect: 'read',
      permission: 'auto',
    },
  ],
  ...patch,
});

export const catalog = (patch: Partial<ConnectorCatalog> = {}): ConnectorCatalog => ({
  entries: [entry()],
  installed: [],
  customLimit: 20,
  ...patch,
});

export const probe = (patch: Partial<McpProbeResult> = {}): McpProbeResult => ({
  serverUrl: 'https://mcp.example.com/mcp',
  auth: 'none',
  authorizationServer: null,
  registration: null,
  serverName: null,
  ...patch,
});
