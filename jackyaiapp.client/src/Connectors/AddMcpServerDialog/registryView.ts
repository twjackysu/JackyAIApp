import type { CredentialsForm } from './addMcpServer';
import type { McpRegistryServer, McpRegistrySearch } from '@/apis/connectorsApis/types';

/** How long the search waits after the last key before asking (D-122). */
export const REGISTRY_SEARCH_DELAY_MS = 400;

const isAuthorization = (header: string) => header.toLowerCase() === 'authorization';

/**
 * The credentials form a registry server starts with: a token when it asks for Authorization,
 * else an API key in the first header it names. The probe still decides whether one is needed.
 */
export function registryCredentials(server: McpRegistryServer): Partial<CredentialsForm> {
  if (server.requiredHeaders.some(isAuthorization)) return { kind: 'bearer' };
  const header = server.requiredHeaders[0];
  return header ? { kind: 'apiKey', headerName: header } : {};
}

/** What the reader will have to bring, in a few words, or null when it names nothing. */
export function registryNeeds(server: McpRegistryServer): string | null {
  if (server.requiredHeaders.some(isAuthorization)) return '需要 token';
  const header = server.requiredHeaders[0];
  return header ? `需要 API key（${header}）` : null;
}

/** The host a server lives at, shown under its name so the reader sees where it goes. */
export function registryHost(server: McpRegistryServer): string {
  try {
    return new URL(server.url).hostname;
  } catch {
    return server.url;
  }
}

/** The line under the list, or null: nothing found, or more than shown. */
export function registryNotice(result: McpRegistrySearch, search: string): string | null {
  const term = search.trim();
  if (result.servers.length === 0)
    return term
      ? `官方目錄裡沒有符合「${term}」、可以直接連線的 remote MCP server。目錄多半是英文，試試英文關鍵字。`
      : '官方目錄目前沒有可以直接連線的 remote MCP server。';
  if (result.more) return `只列出前 ${result.servers.length} 個，輸入更多字可以縮小範圍。`;
  return null;
}
