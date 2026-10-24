import type { ApprovalEvent, ToolEvent } from './useChatStreaming';

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
