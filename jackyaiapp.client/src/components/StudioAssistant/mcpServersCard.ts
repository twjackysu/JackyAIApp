import type { CustomCard } from '@/components/aiChat/cards/toolCard';

/**
 * Studio AI's finds in the official MCP Registry (`find_mcp_server`, an `mcp-servers` card, D-124): what a
 * publisher wrote about their server, so `parseMcpServersCard` keeps only plain text within limits (React
 * escapes it), and each server is a pointer to /connectors, where the reader adds it themselves.
 */

export interface McpServerFind {
  /** The registry's name (io.github.owner/server): the /connectors search that finds exactly it. */
  name: string;
  title: string;
  description: string | null;
  /** Where it lives, shown so the reader sees which site it goes to. */
  host: string | null;
  /** Its https address, only to tell whether the reader has already added it (D-127); never a link. */
  url: string | null;
  /** What adding it will ask for (需要 token), or null when the registry names nothing. */
  needs: string | null;
  /** The catalog on /connectors has it already, to connect from there. */
  inCatalog: boolean;
}

export interface McpServersCardData {
  search: string;
  servers: McpServerFind[];
  more: boolean;
}

const MAX_SERVERS = 10;

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

const text = (v: unknown, max: number): string | null =>
  typeof v === 'string' && v.trim() ? v.trim().slice(0, max) : null;

const httpsUrl = (v: unknown): string | null => {
  const url = text(v, 2000);
  return url?.startsWith('https://') ? url : null;
};

function parseServer(raw: unknown): McpServerFind | null {
  if (!isRecord(raw)) return null;
  const name = text(raw.name, 200);
  const title = text(raw.title, 100) ?? name;
  if (!name || !title) return null;
  return {
    name,
    title,
    description: text(raw.description, 200),
    host: text(raw.host, 253),
    url: httpsUrl(raw.url),
    needs: text(raw.needs, 80),
    inCatalog: raw.inCatalog === true,
  };
}

/** The card's servers as they can be drawn, or null when there is nothing to draw. */
export function parseMcpServersCard(card: CustomCard): McpServersCardData | null {
  const search = text(card.data.search, 50);
  if (!search || !Array.isArray(card.data.servers)) return null;
  const servers = card.data.servers
    .map(parseServer)
    .filter((s): s is McpServerFind => s !== null)
    .slice(0, MAX_SERVERS);
  if (servers.length === 0) return null;
  return { search, servers, more: card.data.more === true };
}
