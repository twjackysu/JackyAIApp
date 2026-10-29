/**
 * The host's side of an MCP App's messages (the `io.modelcontextprotocol/ui` extension, docs/ai-native/design/09 §4):
 * JSON-RPC 2.0 over `postMessage` with the app's frame. A pure function from one message to what the host does, so every
 * method is tested without a frame; `McpAppFrame` checks the message came from its own iframe and carries the steps out.
 */

/** The protocol version this host answers `ui/initialize` with. */
export const APP_PROTOCOL_VERSION = '2026-01-26';

export interface HostState {
  theme: 'light' | 'dark';
  /** An assistant takes the app's `ui/message` into its box (`AppDraftContext`, D-204). */
  takesMessages?: boolean;
}

/** The longest text an app's `ui/message` may put in the box; a longer one is refused, never cut. */
export const MAX_APP_MESSAGE_LENGTH = 2000;

type JsonRpcId = string | number;

export interface JsonRpcReply {
  jsonrpc: '2.0';
  id: JsonRpcId;
  result?: unknown;
  error?: { code: number; message: string };
}

export interface JsonRpcNotification {
  jsonrpc: '2.0';
  method: string;
  params?: unknown;
}

/** What the host does for one message of the app's. */
export type AppStep =
  | { type: 'reply'; message: JsonRpcReply }
  /** The app is ready: now send it the tool's input and result (nothing is sent before, as the spec requires). */
  | { type: 'ready' }
  | { type: 'resize'; height: unknown }
  /** Offer the reader to open this https address; the app cannot open it itself. */
  | { type: 'offerLink'; url: string }
  /** Offer the reader to put this text in the box (`ui/message`); the app cannot send it itself. */
  | { type: 'offerMessage'; text: string }
  /** Ask the server to run one of the connector's tools for the app (only one that reads, the server decides), then reply. */
  | { type: 'callTool'; id: JsonRpcId; name: string; arguments: Record<string, unknown> };

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

const reply = (id: JsonRpcId, result: unknown): AppStep => ({
  type: 'reply',
  message: { jsonrpc: '2.0', id, result },
});
const refuse = (id: JsonRpcId, code: number, message: string): AppStep => ({
  type: 'reply',
  message: { jsonrpc: '2.0', id, error: { code, message } },
});

/** An https address the reader may be offered (no credentials in it), as the browser writes it; null otherwise. */
export function linkOf(raw: unknown): string | null {
  if (typeof raw !== 'string' || raw.length > 2000) return null;
  try {
    const url = new URL(raw);
    return url.protocol === 'https:' && !url.username && !url.password ? url.href : null;
  } catch {
    return null;
  }
}

/**
 * The text of a `ui/message` (`role: "user"`, its content one block or a list of them): the text blocks, a line each,
 * trimmed; null for any other role, no text, or more than `MAX_APP_MESSAGE_LENGTH`.
 */
export function appMessageTextOf(params: Record<string, unknown>): string | null {
  if (params.role !== 'user') return null;
  const blocks = Array.isArray(params.content) ? params.content : [params.content];
  const texts = blocks.flatMap((b) =>
    isRecord(b) && b.type === 'text' && typeof b.text === 'string' ? [b.text] : [],
  );
  const text = texts.join('\n').trim();
  return text && text.length <= MAX_APP_MESSAGE_LENGTH ? text : null;
}

/** What `ui/initialize` is answered with: who the host is and what it offers the app. */
export function initializeResult(state: HostState) {
  return {
    protocolVersion: APP_PROTOCOL_VERSION,
    hostInfo: { name: 'Jacky Studio', version: '1.0' },
    hostCapabilities: {
      openLinks: {},
      serverTools: {},
      ...(state.takesMessages && { message: { text: {} } }),
    },
    hostContext: {
      theme: state.theme,
      displayMode: 'inline',
      availableDisplayModes: ['inline'],
      locale: 'zh-TW',
      timeZone: 'Asia/Taipei',
      platform: 'web',
      containerDimensions: { maxHeight: 640 },
    },
  };
}

/** The steps for one message the app sent; none for anything that is not JSON-RPC 2.0. */
export function stepsFor(data: unknown, state: HostState): AppStep[] {
  if (!isRecord(data) || data.jsonrpc !== '2.0' || typeof data.method !== 'string') return [];
  const id = typeof data.id === 'string' || typeof data.id === 'number' ? data.id : null;
  const params = isRecord(data.params) ? data.params : {};

  if (id === null) {
    // Notifications: no answer.
    if (data.method === 'ui/notifications/initialized') return [{ type: 'ready' }];
    if (data.method === 'ui/notifications/size-changed')
      return [{ type: 'resize', height: params.height }];
    return [];
  }

  switch (data.method) {
    case 'ui/initialize':
      return [reply(id, initializeResult(state))];
    case 'ping':
      return [reply(id, {})];
    case 'ui/open-link': {
      const url = linkOf(params.url);
      return url
        ? [{ type: 'offerLink', url }, reply(id, {})]
        : [refuse(id, -32602, 'Only https links can be opened.')];
    }
    case 'ui/message': {
      if (!state.takesMessages)
        return [refuse(id, -32601, 'Method not supported by this host: ui/message')];
      const text = appMessageTextOf(params);
      return text
        ? [{ type: 'offerMessage', text }, reply(id, {})]
        : [
            refuse(
              id,
              -32602,
              `ui/message needs a user's text of at most ${MAX_APP_MESSAGE_LENGTH} characters.`,
            ),
          ];
    }
    case 'tools/call': {
      const name =
        typeof params.name === 'string' && params.name.length <= 128 ? params.name : null;
      const args =
        params.arguments === undefined ? {} : isRecord(params.arguments) ? params.arguments : null;
      return name && args
        ? [{ type: 'callTool', id, name, arguments: args }]
        : [refuse(id, -32602, 'tools/call needs a name and object arguments.')];
    }
    case 'ui/request-display-mode':
      return [reply(id, { mode: 'inline' })];
    case 'ui/update-model-context':
      // Not passed to the model (design/09 §2): an app's words reach it only through the reader.
      return [reply(id, {})];
    default:
      return [refuse(id, -32601, `Method not supported by this host: ${data.method.slice(0, 60)}`)];
  }
}

/** The notifications that hand the app its tool's call once it is ready: the arguments, then the result. */
export function toolNotifications(
  input: Record<string, unknown>,
  result: Record<string, unknown>,
): JsonRpcNotification[] {
  return [
    { jsonrpc: '2.0', method: 'ui/notifications/tool-input', params: { arguments: input } },
    { jsonrpc: '2.0', method: 'ui/notifications/tool-result', params: result },
  ];
}

/** The notification that tells an app the host's light or dark changed (`hostContext.theme`, D-210). */
export const HOST_CONTEXT_CHANGED = 'ui/notifications/host-context-changed';

/** What the app is sent when the reader's light or dark changes: only the part of the host's context that changed. */
export function hostContextChanged(theme: HostState['theme']): JsonRpcNotification {
  return { jsonrpc: '2.0', method: HOST_CONTEXT_CHANGED, params: { theme } };
}
