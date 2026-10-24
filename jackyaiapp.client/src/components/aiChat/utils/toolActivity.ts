/**
 * The tools an answer used, as the reader sees them: one step per call, in order, each running,
 * done or failed. Built from the stream's `tool_use` and `tool_result` events
 * (docs/ai-native/design/04 §1.5). A hosted tool (tw-stock, run by Anthropic's MCP connector) sends
 * only `tool_use` — it has no result event — so its step stays running until the answer ends,
 * when `settleToolSteps` closes every step still open.
 */
import { parseToolCard, type ToolCard } from '../cards/toolCard';

import type { ToolEvent } from '@/hooks/useChatStreaming';

export type { ToolEvent };

export type ToolStepStatus = 'running' | 'done' | 'failed';

export interface ToolStep {
  /** Local key, unique within the answer. */
  key: string;
  /** The server's id for the call, when it sends one (our own tools do; hosted ones do not). */
  id?: string;
  name: string;
  /** What the reader is shown: the server's display name, or one made from the tool's name. */
  label: string;
  status: ToolStepStatus;
  durationMs?: number;
  /** What its result shows in the answer (a table, a link…), checked by `parseToolCard`. */
  card?: ToolCard;
}

/** A tool name the server gave no display name for, made readable: `get_stock_history` → `stock history`. */
export const labelFromName = (name: string): string => {
  // A connector's tools come prefixed with its key (`c3__search`): the reader knows the tool, not the key.
  const bare = name.includes('__') ? name.slice(name.lastIndexOf('__') + 2) : name;
  const words = bare.replace(/^get_/, '').replace(/[_-]+/g, ' ').trim();
  return words || name;
};

/** The steps after one more event; an event that matches nothing leaves them as they are. */
export function applyToolEvent(steps: readonly ToolStep[], event: ToolEvent): ToolStep[] {
  if (event.type === 'tool_use') {
    return [
      ...steps,
      {
        key: event.id ?? `${event.name}-${steps.length}`,
        id: event.id,
        name: event.name,
        label: event.displayName || labelFromName(event.name),
        status: 'running',
      },
    ];
  }
  const index = steps.findIndex((s) => s.id === event.id && s.status === 'running');
  if (index < 0) return [...steps];
  // A failed call shows no card: what it returned is not a result.
  const card = event.ok ? parseToolCard(event.card) : null;
  return steps.map((s, i) =>
    i === index
      ? {
          ...s,
          status: event.ok ? 'done' : 'failed',
          durationMs: event.durationMs,
          ...(card ? { card } : {}),
        }
      : s,
  );
}

/**
 * The steps once the answer has ended: those still running are done if it answered (a hosted
 * tool's result is in the answer), failed if it did not.
 */
export const settleToolSteps = (steps: readonly ToolStep[], answered: boolean): ToolStep[] =>
  steps.map((s) => (s.status === 'running' ? { ...s, status: answered ? 'done' : 'failed' } : s));
