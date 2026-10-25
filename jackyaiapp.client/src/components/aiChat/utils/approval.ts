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
  /** When the question arrived (ms since the epoch, the client's clock): the turn waits at most
   *  `APPROVAL_TIMEOUT_MS` from about then. */
  askedAt?: number;
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

/** How long the server waits for an answer before counting every call as a no (`ClientToolRunner.ApprovalTimeout`). */
export const APPROVAL_TIMEOUT_MS = 3 * 60 * 1000;

/**
 * What the card says about the time left to answer, or nothing when it does not know when it was asked. The server
 * started counting a moment before the question arrived, so this is never later than its own limit. Under a minute
 * it counts the seconds (`urgent`); at the end, the server's no is on its way.
 */
export const approvalTimeLeft = (
  askedAt: number | undefined,
  now: number,
): { text: string; urgent: boolean } | null => {
  if (askedAt === undefined) return null;
  const left = Math.ceil((askedAt + APPROVAL_TIMEOUT_MS - now) / 1000);
  if (left <= 0) return { text: '時間到了，這次不會執行', urgent: true };
  if (left <= 60) return { text: `${left} 秒內沒有回覆，這次就不執行`, urgent: true };
  return { text: `${Math.ceil(left / 60)} 分鐘內沒有回覆，這次就不執行`, urgent: false };
};
