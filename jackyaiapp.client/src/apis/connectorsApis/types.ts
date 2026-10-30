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
  /** Never runs on its own, though it can be undone (Studio AI's `remember`): no 「自動」. */
  alwaysAsks?: boolean;
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
  /** For a manual registration: the redirect URI to register on the OAuth app the reader makes there. */
  redirectUri?: string | null;
}

/**
 * A remote MCP server listed in the official MCP Registry that the site can connect to (D-122).
 * Everything in it is what someone published there: shown as text, its links https only.
 */
export interface McpRegistryServer {
  /** The registry's name, reverse-DNS (io.github.owner/server). */
  name: string;
  title: string;
  description: string;
  /** Its Streamable HTTP address, as an installation keeps it. */
  url: string;
  websiteUrl: string | null;
  repositoryUrl: string | null;
  version: string | null;
  /** Headers it says a client must send: an API key's, or Authorization for a token. */
  requiredHeaders: string[];
  /** The site's own catalog entry at the same address, if there is one. */
  catalogKey: string | null;
}

export interface McpRegistrySearch {
  servers: McpRegistryServer[];
  /** More matches than shown: a longer search narrows them. */
  more: boolean;
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

/**
 * A personal access token another AI (Claude Desktop, Cursor…) uses the site's own MCP server with
 * (`/api/mcp`). The token itself is shown only once, when it is made.
 */
export interface SiteAccessToken {
  id: string;
  name: string;
  /** Its start and end ("jsk_AbC…xYz9"), to tell it from the reader's others. */
  hint: string;
  /** Whether the site's tools that add or change something are offered too. */
  allowWrites: boolean;
  /** Made by another AI's OAuth sign-in (Claude.ai, ChatGPT), named after its client, not on this page. */
  viaOAuth: boolean;
  createdAt: string;
  expiresAt: string;
  lastUsedAt: string | null;
}

export interface SiteAccessTokenList {
  tokens: SiteAccessToken[];
  /** The most tokens one reader may have. */
  limit: number;
}

export interface CreateSiteAccessTokenRequest {
  name: string;
  allowWrites: boolean;
  /** 30, 90 or 365. */
  expiresInDays: number;
}

export interface CreatedSiteAccessToken {
  info: SiteAccessToken;
  /** The token itself: the only time it is shown. */
  token: string;
}

/** A connector's MCP App as a chat draws it (the server's `McpAppDto`, design/09). */
export interface McpApp {
  html: string;
  /** https origins the app may connect to (its CSP's connect-src). */
  connectDomains: string[];
  /** https origins it may load scripts, styles, images, fonts and media from. */
  resourceDomains: string[];
  prefersBorder: boolean | null;
  /** The connector's name, and its server's host, for the line over the frame. */
  connector: string;
  host: string;
}

/** What a call an app asked came to: a `CallToolResult` as the app reads it. */
export interface McpAppCallResult {
  result: Record<string, unknown>;
}
