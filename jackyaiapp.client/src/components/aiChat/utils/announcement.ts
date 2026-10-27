/**
 * What the chat says to a screen reader, and when (D-181). The reader's focus stays in the message box
 * while an answer is written, so without this nothing tells them the answer is done, failed, or — worse —
 * that a write is waiting for their yes (the turn gives up after three minutes). Only changes of state are
 * said, never the text as it streams: a live region that repeated every piece would drown the box.
 */

import type { Message } from '../types';
import type { MessageApproval } from './approval';

import { STOPPED_NOTE } from '@/hooks/useChatStreaming';

export const WORKING_ANNOUNCEMENT = 'AI 回覆中…';
export const ANSWERED_ANNOUNCEMENT = '回覆完成。';
export const STOPPED_ANNOUNCEMENT = '已停止回覆。';

/** How much of a failure's wording is said: its reason, not a page of text. */
const MAX_FAILURE_CHARS = 150;

/** A write waiting for the reader, by the names on its card. */
export const approvalAnnouncement = (approval: MessageApproval): string => {
  const names = [...new Set(approval.request.calls.map((c) => c.displayName || c.tool))];
  return `需要你的確認：${names.join('、')}。確認卡在回答下方。`;
};

/** What a turn that just ended came to, from its last message (the assistant's). */
export const finishedAnnouncement = (last: Message | undefined): string => {
  if (!last || last.isUser) return '';
  if (last.answered) return ANSWERED_ANNOUNCEMENT;
  if (last.content.trimEnd().endsWith(STOPPED_NOTE)) return STOPPED_ANNOUNCEMENT;
  const reason = last.content.trim();
  return reason ? `回覆沒有完成：${reason.slice(0, MAX_FAILURE_CHARS)}` : '回覆沒有完成。';
};

/** The open card waiting for the reader's decision, if any (not one being sent). */
export const pendingApprovalOf = (messages: readonly Message[]): MessageApproval | undefined =>
  messages.find((m) => m.approval?.state === 'pending')?.approval;
