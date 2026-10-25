/**
 * A write the assistant wants to make, waiting for the reader's yes or no (docs/ai-native/design/04
 * §1.3): the server sends `approval_required` and holds the turn until the decisions arrive, then
 * carries on in the same stream (`approval_resolved`).
 */
import type { ToolPermissionRequest } from '@/apis/connectorsApis/types';
import type { ApprovalCallEvent } from '@/hooks/useChatStreaming';

/** One call to decide: its connector's key ("site" for the site's own tools) and what the reader
 *  calls it ("本站・單字庫", "Jira"), the tool, whether it reads, writes or cannot be undone, and
 *  its arguments as the model wrote them. */
export type ApprovalCall = ApprovalCallEvent;

export interface PendingApproval {
  pendingId: string;
  calls: ApprovalCall[];
  /** The conversation has read outside (untrusted) content: every write needs a yes. */
  tainted?: boolean;
}

export interface ApprovalDecision {
  toolUseId: string;
  approve: boolean;
}

/** Where a card is: waiting for the reader, sent, or answered by the server. */
export type ApprovalState = 'pending' | 'sending' | 'resolved' | 'failed';

/** An approval as an answer carries it. */
export interface MessageApproval {
  request: PendingApproval;
  state: ApprovalState;
  decisions?: ApprovalDecision[];
}

/** The site's own tools: no "on ○○" line, the action happens here. */
export const SITE_CONNECTOR = 'site';

/** An argument as one short line: text as it is, anything else as compact JSON, cut at 120. */
export const formatArgument = (value: unknown): string => {
  const text = typeof value === 'string' ? value : JSON.stringify(value);
  if (text === undefined) return String(value);
  return text.length > 120 ? `${text.slice(0, 119)}…` : text;
};

/** The decisions to send: one per call, a call left undecided counted as a no. */
export const decisionsFor = (
  calls: readonly ApprovalCall[],
  chosen: Readonly<Record<string, boolean>>,
): ApprovalDecision[] =>
  calls.map((c) => ({ toolUseId: c.toolUseId, approve: chosen[c.toolUseId] === true }));

/**
 * Whether the card offers to let the call's tool run on its own from now on: a site tool's write (an outside
 * connector's tools are set on the connectors page, and a destructive one never runs on its own), and only in a
 * conversation that has not read outside content, where the server would ask anyway.
 */
export const canAlwaysAllow = (call: ApprovalCall, tainted?: boolean): boolean =>
  call.connector === SITE_CONNECTOR && call.effect === 'write' && !call.alwaysAsks && !tainted;

/** The connectors page's request that lets the call's tool run without asking (`PUT tool-permissions`). */
export const alwaysAllowRequest = (call: ApprovalCall): ToolPermissionRequest => ({
  definitionKey: SITE_CONNECTOR,
  installationId: null,
  toolName: call.tool,
  permission: 'auto',
});

/**
 * Whether a card is still waiting on the reader's answer in its turn's own request: asked, being sent, or
 * sent and failed (the turn still waits). Reloading or closing the page then ends that request, and the turn
 * with it, so nothing on the card is done (D-026, D-105).
 */
export const hasOpenApproval = (messages: readonly { approval?: MessageApproval }[]): boolean =>
  messages.some((m) => m.approval !== undefined && m.approval.state !== 'resolved');
