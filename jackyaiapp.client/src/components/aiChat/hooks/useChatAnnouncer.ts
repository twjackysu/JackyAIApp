import { useEffect, useRef, useState } from 'react';

import {
  approvalAnnouncement,
  finishedAnnouncement,
  pendingApprovalOf,
  WORKING_ANNOUNCEMENT,
} from '../utils/announcement';

import type { Message } from '../types';

/**
 * The text of the chat's live region (`utils/announcement`): set when an answer starts, ends (done, stopped
 * or failed) and when a confirmation card starts waiting, and nowhere else — opening an old conversation
 * says nothing, nor does each piece of a streaming answer.
 */
export function useChatAnnouncer(messages: readonly Message[], busy: boolean): string {
  const [text, setText] = useState('');
  const wasBusy = useRef(busy);
  useEffect(() => {
    if (busy === wasBusy.current) return;
    wasBusy.current = busy;
    setText(busy ? WORKING_ANNOUNCEMENT : finishedAnnouncement(messages[messages.length - 1]));
    // Only the change of `busy` is said; the messages are read as they are at that moment.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [busy]);

  const pending = pendingApprovalOf(messages);
  const pendingId = pending?.request.pendingId;
  const said = useRef<string | undefined>(undefined);
  useEffect(() => {
    if (!pending || said.current === pendingId) return;
    said.current = pendingId;
    setText(approvalAnnouncement(pending));
  }, [pending, pendingId]);

  return text;
}
