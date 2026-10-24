export interface ConnectorStatus {
  provider: string;
  providerDisplayName: string;
  isConnected: boolean;
  services: string[];
  expiresAt?: string;
  requiresReconnection: boolean;
}

export interface ConnectResponse {
  redirectUrl: string;
}

export interface RefreshResponse {
  message: string;
}

export interface DisconnectResponse {
  message: string;
}

export interface AccessTokenResponse {
  provider: string;
  accessToken: string;
  retrievedAt: string;
}

export interface RefreshTokenResponse {
  provider: string;
  refreshToken: string;
  retrievedAt: string;
}

export interface CustomConnectRequest {
  clientId?: string;
  clientSecret?: string;
  tenantId?: string;
  scopes?: string;
}

// ---- AI connectors (docs/ai-native/design/03 §7) ----

/** What one tool does to the world it reaches; the server decides it, the page only shows it. */
export type ToolEffect = 'read' | 'write' | 'destructive';
/** What the AI may do with one tool: run it on its own, ask the reader first, or not see it. */
export type ToolPermission = 'auto' | 'confirm' | 'disabled';

/** How a connector proves who it is to the service behind it. */
export type ConnectorAuthKind = 'none' | 'apiKey' | 'bearer' | 'oauth' | 'existingProvider';
export type ConnectorCategory = 'site' | 'data' | 'dev' | 'productivity' | 'finance';
/** `disabled`: the reader turned it off; `needsAuth`: the service no longer takes its token. */
export type ConnectorHealth = 'ok' | 'needsAuth' | 'error' | 'unknown' | 'disabled';

export interface ConnectorTool {
  name: string;
  title: string | null;
  description: string;
  effect: ToolEffect;
  permission: ToolPermission;
}

/** A connector the reader has (or, for the site's own tools, always has). */
export interface InstalledConnector {
  /** Null for an always-on connector whose settings were never changed (no row yet). */
  id: string | null;
  definitionKey: string;
  displayName: string;
  description: string;
  category: ConnectorCategory;
  serverUrl: string | null;
  authKind: ConnectorAuthKind;
  health: ConnectorHealth;
  alwaysOn: boolean;
  /** Plain words for the owner, never a token. */
  lastError: string | null;
  lastTestedAt: string | null;
  scopes: string[];
  tools: ConnectorTool[];
}

/** One kind of connector the reader may add. */
export interface CatalogEntry {
  key: string;
  displayName: string;
  description: string;
  category: ConnectorCategory;
  publisher: string;
  authKind: ConnectorAuthKind;
  docsUrl: string | null;
  /** The reader already has it (a built-in one can be added once). */
  installed: boolean;
}

export interface ConnectorCatalog {
  entries: CatalogEntry[];
  installed: InstalledConnector[];
  /** How many MCP servers of their own one reader may add. */
  customLimit: number;
}

/** What the server found at an MCP URL before anything is saved. */
export interface McpProbeResult {
  /** The URL as it will be stored (https, no fragment, no trailing slash). */
  serverUrl: string;
  auth: 'none' | 'oauth' | 'apiKey';
  /** For OAuth: who signs the reader in, and how we become its client. */
  authorizationServer: string | null;
  registration: 'cimd' | 'dcr' | 'manual' | null;
  /** The server's own name for itself, when it says one. */
  serverName: string | null;
}

export interface McpCredentials {
  /** For an API key: the header it goes in (Authorization for a bearer token). */
  headerName?: string;
  value?: string;
  /** For OAuth with a server we cannot register with on our own. */
  clientId?: string;
  clientSecret?: string;
}

export interface CreateInstallationRequest {
  /** A catalog entry's key, or `custom` for an MCP server of the reader's own. */
  definitionKey: string;
  displayName?: string;
  serverUrl?: string;
  authKind: Exclude<ConnectorAuthKind, 'existingProvider'>;
  credentials?: McpCredentials;
}

export interface ConnectionTestResult {
  ok: boolean;
  latencyMs: number;
  error: string | null;
  connector: InstalledConnector;
}

export interface ToolPermissionRequest {
  definitionKey: string;
  /** Null for an always-on connector with no row yet: the server makes one. */
  installationId: string | null;
  toolName: string;
  permission: ToolPermission;
}
