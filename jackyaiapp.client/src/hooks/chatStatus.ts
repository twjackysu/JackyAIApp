import type { ApprovalEvent, ToolEvent } from './useChatStreaming';

/**
 * A tool call named for the reader by the app's own labels (`AiChatConfig.toolLabels`) when the server
 * gave it no display name: a hosted tool (Finance's tw-stock, run by Anthropic's MCP connector) has none.
 * The server's display name always wins; a tool the labels do not know keeps its own name.
 */
export function withToolLabel(
  event: ToolEvent,
  labels: Readonly<Record<string, string>> | undefined,
): ToolEvent {
  if (event.type !== 'tool_use' || event.displayName || !labels) return event;
  const label = Object.prototype.hasOwnProperty.call(labels, event.name)
    ? labels[event.name]
    : undefined;
  return label ? { ...event, displayName: label } : event;
}

/** What the chat's header says the turn is doing, after one of its events. */
export interface ChatStatus {
  currentEvent: string;
  statusText: string;
}

export const ANSWERING = '正在生成回應...';
export const WAITING_FOR_READER = '⏸ 等你確認…';

/**
 * The header's line after a tool or approval event: the tool by the name the reader is shown (a site
 * or connector tool's display name; a hosted tool, which has none, by its own name as before), the
 * answer going on once a result or the reader's decisions are in, and the turn waiting on the reader
 * while a write is put to them.
 */
export function statusAfter(event: ToolEvent | ApprovalEvent): ChatStatus {
  switch (event.type) {
    case 'tool_use':
      return {
        currentEvent: 'tool_use',
        statusText: event.displayName
          ? `🔄 正在使用「${event.displayName}」…`
          : `🔄 正在查詢 ${event.name}...`,
      };
    case 'approval_required':
      return { currentEvent: 'approval', statusText: WAITING_FOR_READER };
    case 'tool_result':
    case 'approval_resolved':
      return { currentEvent: event.type, statusText: ANSWERING };
  }
}
