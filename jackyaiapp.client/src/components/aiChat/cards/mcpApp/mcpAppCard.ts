import type { CustomCard } from '../toolCard';

/**
 * A connector's MCP App in an answer (docs/ai-native/design/09 §3): which connector, tool and app (`ui://…`), and the
 * call's arguments and result as the app is handed them. The server sends no HTML in it; `McpAppCardView` asks for the
 * app when it draws the card. Everything here came from an outside server, so it is read as data and held to limits.
 */
export interface McpAppCardData {
  installationId: string;
  connector: string;
  tool: string;
  title: string;
  resourceUri: string;
  input: Record<string, unknown>;
  /** A `CallToolResult` as the app reads it (`ui/notifications/tool-result`). */
  result: Record<string, unknown>;
}

export const MCP_APP_CARD_KIND = 'mcp-app';

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

/**
 * One line of plain text, at most `max` characters: control and format characters dropped, so a name cannot reorder the
 * caption around it (a right-to-left override) and pass itself off as whose app it is.
 */
export function line(v: unknown, max: number): string | null {
  if (typeof v !== 'string') return null;
  const plain = v.replace(/[\p{Cc}\p{Cf}]+/gu, ' ').trim();
  return plain ? Array.from(plain).slice(0, max).join('') : null;
}

/** A `ui://` URI as the server keeps it (`McpToolClient.AppUriOf`): no white space or control characters, 512 at most. */
export function appUriOf(v: unknown): string | null {
  return typeof v === 'string' &&
    v.startsWith('ui://') &&
    v.length > 5 &&
    v.length <= 512 &&
    !/[\s\p{C}]/u.test(v)
    ? v
    : null;
}

/** The card's data, or null when it is not one this chat can draw. */
export function parseMcpAppCard(card: CustomCard): McpAppCardData | null {
  if (card.kind !== MCP_APP_CARD_KIND) return null;
  const d = card.data;
  const installationId =
    typeof d.installationId === 'string' && /^[\w-]{1,64}$/.test(d.installationId)
      ? d.installationId
      : null;
  const resourceUri = appUriOf(d.resourceUri);
  const tool = line(d.tool, 128);
  if (!installationId || !resourceUri || !tool || !isRecord(d.input) || !isRecord(d.result))
    return null;
  return {
    installationId,
    connector: line(d.connector, 60) ?? 'MCP server',
    tool,
    title: line(d.title, 100) ?? tool,
    resourceUri,
    input: d.input,
    result: d.result,
  };
}

/** The result's text blocks, for when the app is not drawn (at most 2000 characters). */
export function resultText(result: Record<string, unknown>): string {
  const content = Array.isArray(result.content) ? result.content : [];
  return content
    .map((b) => (isRecord(b) && b.type === 'text' && typeof b.text === 'string' ? b.text : null))
    .filter((t): t is string => t !== null)
    .join('\n')
    .slice(0, 2000);
}
