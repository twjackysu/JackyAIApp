import { useCallback, useEffect, useRef, useState } from 'react';

import { ANSWERING, THINKING, statusAfter, withToolLabel } from './chatStatus';
import { finishSseLines, splitSseLines } from './sseLines';

import type { ToolEffect } from '@/apis/connectorsApis/types';

import { ChatErrorCode, chatErrorMessage } from '@/apis/chatbotApis/chatErrors';
import { getApiErrorMessage } from '@/apis/utils/errorMessage';
import { useAppDispatch } from '@/redux/hooks';
import { openUsageLimitModal } from '@/redux/uiSlice';

export interface StreamingStatus {
  isLoading: boolean;
  currentEvent: string | null;
  statusText: string;
}

/** A tool call starting, or one of our own tools finishing (hosted tools send no result). */
export type ToolEvent =
  | { type: 'tool_use'; name: string; id?: string; displayName?: string }
  | {
      type: 'tool_result';
      id: string;
      name: string;
      ok: boolean;
      durationMs?: number;
      /** What the result shows in the answer; unchecked until `parseToolCard`. */
      card?: unknown;
      /** A path on this site the reader's page goes to now (Studio AI's `open_page` when asked to take them
       *  there); unchecked until `sitePathOf`. */
      navigate?: unknown;
      /** A question for the assistant of the page `navigate` goes to (Studio AI handing a task to /data-analysis's or
       *  /finance's own); unchecked until `handoffOf`. */
      handoff?: unknown;
    };

/** A write waiting for the reader's decision, and the server saying the decisions arrived
 *  (components/aiChat/utils/approval). The stream stays open in between. */
export type ApprovalEvent =
  | { type: 'approval_required'; pendingId: string; calls: ApprovalCallEvent[]; tainted?: boolean }
  | {
      type: 'approval_resolved';
      pendingId: string;
      decisions: { toolUseId: string; approve: boolean }[];
    };

/** One call in an `approval_required` event, as the server sends it. */
export interface ApprovalCallEvent {
  toolUseId: string;
  connector: string;
  connectorName?: string;
  tool: string;
  displayName: string;
  effect: ToolEffect;
  /** Never to be let run on its own (Studio AI's `remember`, D-101): no 「以後直接執行」, no 全部允許. */
  alwaysAsks?: boolean;
  input: Record<string, unknown>;
  /** What the call would do, in words, from the site's own tool (never a connector's); null when it has none. */
  summary?: string | null;
}

/** Our own SSE event shape from ChatbotController — written by AnthropicChatService through
 *  SseStream. A failure carries only a code; `chatErrorMessage` turns it into words. */
type ChatStreamEvent =
  | { type: 'start'; conversationId: string; userMessageId?: number }
  | { type: 'delta'; text: string }
  /** A piece of the summary of the model's thinking (D-136): not the answer, never stored. */
  | { type: 'thinking'; text: string }
  | ToolEvent
  | ApprovalEvent
  | {
      type: 'done';
      conversationId: string;
      truncated?: boolean;
      stopped?: boolean;
      assistantMessageId?: number | null;
    }
  | { type: 'error'; code?: string };

export interface ChatTurn {
  message: string;
  conversationId: string | undefined;
  /** Uploaded files (useUploadChatAttachmentMutation) to send with this message. */
  attachmentIds: number[];
  /** This message is an edit of the reader's stored message with this id: the server removes it and
   *  everything after it, and sends its attachments again with the new wording. */
  editMessageId?: number;
  /** The message quotes text the reader selected on a page (Studio AI's selection bar), possibly someone
   *  else's words: the server counts the conversation as having read outside text, so its writes ask first. */
  quotesPageText?: boolean;
  /** The conversation the turn is in — known before any answer, so even a new one can be stopped. */
  onStart?: (conversationId: string, userMessageId?: number) => void;
  /** The answer so far, each time it grows. */
  onUpdate: (content: string) => void;
  /** A piece of the summary of the model's thinking, as it comes (not the answer; see `utils/thinking`). */
  onThinking?: (piece: string) => void;
  /** `truncated`: the answer ran into the length limit and is cut short. */
  onComplete: (conversationId?: string, truncated?: boolean, assistantMessageId?: number) => void;
  /** The reader stopped the turn; `partial` is what had been written by then (the server keeps it). */
  onStopped: (partial: string, assistantMessageId?: number) => void;
  onError: (error: string) => void;
  /** A tool call started or finished. */
  onToolEvent?: (event: ToolEvent) => void;
  /** A write needs the reader's decision, or the decisions arrived. */
  onApprovalEvent?: (event: ApprovalEvent) => void;
}

/** What the server keeps of a stopped answer, ending it (AnthropicChatService.StoppedContent). */
export const STOPPED_NOTE = '（已中斷）';

/** A stopped answer as the server stores it, so the bubble reads the same before and after a reload. */
export const stoppedContent = (partial: string) =>
  partial.trim() ? `${partial.trimEnd()}\n\n${STOPPED_NOTE}` : STOPPED_NOTE;

const stopOnServer = (conversationId: string) =>
  fetch(`/api/chatbot/conversations/${encodeURIComponent(conversationId)}/stop`, {
    method: 'POST',
  }).catch(() => {
    // The turn ends anyway once the request is dropped; the stop call only makes it immediate.
  });

export interface ChatStreamingOptions {
  /** The app's own wording for some failure codes, in place of the shared one (`chatErrorMessage`). */
  errorMessages?: Partial<Record<ChatErrorCode, string>>;
  /** The server's chat profile the turns are for (`AiChatConfig.profile`); the server's default
   *  (finance) when left out. */
  profile?: string;
  /** The page's context for this turn's tools (`AiChatConfig.pageContext`), as it is when a message is sent. */
  pageContext?: Readonly<Record<string, string>>;
  /** Words for tools the server sends no display name for (`AiChatConfig.toolLabels`), by tool name. */
  toolLabels?: Readonly<Record<string, string>>;
}

export const useChatStreaming = ({
  errorMessages,
  profile,
  pageContext,
  toolLabels,
}: ChatStreamingOptions = {}) => {
  const dispatch = useAppDispatch();
  const [streamingStatus, setStreamingStatus] = useState<StreamingStatus>({
    isLoading: false,
    currentEvent: null,
    statusText: '',
  });

  // The turn being answered: how to drop its request, and what the server calls its conversation.
  const turnRef = useRef<{
    abort: AbortController;
    conversationId?: string;
    stopRequested: boolean;
  } | null>(null);

  /**
   * Stops the answer being written. The server is told first (the dropped request alone may not
   * reach it promptly through the proxy in front of it), then the request is dropped. Before the
   * server has named the conversation, the stop waits for that.
   */
  const stop = useCallback(() => {
    const turn = turnRef.current;
    if (!turn || turn.stopRequested) return;
    turn.stopRequested = true;
    if (turn.conversationId) {
      void stopOnServer(turn.conversationId);
      turn.abort.abort();
    }
  }, []);

  // Leaving the page that holds the chat stops its answer: nobody is left to read it, and the server
  // would go on writing (and paying for) it.
  useEffect(() => stop, [stop]);

  const sendStreamingMessage = useCallback(
    async ({
      message,
      conversationId,
      attachmentIds,
      editMessageId,
      quotesPageText,
      onStart,
      onUpdate,
      onThinking,
      onComplete,
      onStopped,
      onError,
      onToolEvent,
      onApprovalEvent,
    }: ChatTurn) => {
      const turn = { abort: new AbortController(), conversationId, stopRequested: false };
      turnRef.current = turn;
      setStreamingStatus({ isLoading: true, currentEvent: null, statusText: '正在連接...' });
      let accumulatedContent = '';

      try {
        const response = await fetch('/api/chatbot/chat', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Accept: 'text/event-stream',
            'Cache-Control': 'no-cache',
          },
          body: JSON.stringify({
            message,
            conversationId,
            attachmentIds,
            editMessageId,
            profile,
            context: pageContext,
            ...(quotesPageText && { quotesPageText: true }),
          }),
          signal: turn.abort.signal,
        });

        if (!response.ok) {
          const text = await response.text().catch(() => '');
          // The body is the app's standard `{ error: { code, message } }` envelope when the
          // backend got far enough to build one, plain text otherwise (e.g. "Not authenticated.").
          let parsedBody: unknown = text;
          try {
            parsedBody = text ? JSON.parse(text) : text;
          } catch {
            // Not JSON — keep the raw text, getApiErrorMessage handles that shape too.
          }
          if (response.status === 429) {
            dispatch(openUsageLimitModal());
          }
          throw new Error(getApiErrorMessage({ status: response.status, data: parsedBody }));
        }

        if (!response.body) throw new Error('No response body');

        const reader = response.body.getReader();
        const decoder = new TextDecoder();

        setStreamingStatus((prev) => ({ ...prev, statusText: ANSWERING }));

        // Handles one event; true once the turn has ended (done or error).
        const handle = (data: string): boolean => {
          try {
            const event = JSON.parse(data) as ChatStreamEvent;

            switch (event.type) {
              case 'start':
                turn.conversationId = event.conversationId;
                onStart?.(event.conversationId, event.userMessageId);
                // Stop was pressed before the server had named the conversation.
                if (turn.stopRequested) {
                  void stopOnServer(event.conversationId);
                  turn.abort.abort();
                }
                break;
              case 'delta':
                accumulatedContent += event.text;
                onUpdate(accumulatedContent);
                // The header said it was thinking; the answer is being written now.
                setStreamingStatus((prev) =>
                  prev.currentEvent === 'thinking'
                    ? { ...prev, currentEvent: 'delta', statusText: ANSWERING }
                    : prev,
                );
                break;
              case 'thinking':
                if (!event.text) break;
                onThinking?.(event.text);
                setStreamingStatus((prev) =>
                  prev.currentEvent === 'thinking'
                    ? prev
                    : { ...prev, currentEvent: 'thinking', statusText: THINKING },
                );
                break;
              case 'tool_use':
              case 'tool_result': {
                const named = withToolLabel(event, toolLabels);
                setStreamingStatus((prev) => ({ ...prev, ...statusAfter(named) }));
                onToolEvent?.(named);
                break;
              }
              case 'approval_required':
              case 'approval_resolved':
                setStreamingStatus((prev) => ({ ...prev, ...statusAfter(event) }));
                onApprovalEvent?.(event);
                break;
              case 'done':
                if (event.stopped)
                  onStopped(accumulatedContent, event.assistantMessageId ?? undefined);
                else
                  onComplete(
                    event.conversationId,
                    event.truncated === true,
                    event.assistantMessageId ?? undefined,
                  );
                return true;
              case 'error':
                onError(
                  (event.code && errorMessages?.[event.code as ChatErrorCode]) ||
                    chatErrorMessage(event.code),
                );
                return true;
            }
          } catch (e) {
            console.warn('Failed to parse SSE line:', data, e);
          }
          return false;
        };

        // A chunk can end mid-line; the unfinished line waits for the next one (splitSseLines).
        let rest = '';
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          const lines = splitSseLines(rest, decoder.decode(value, { stream: true }));
          rest = lines.rest;
          for (const data of lines.data) if (handle(data)) return;
        }
        for (const data of finishSseLines(rest + decoder.decode())) if (handle(data)) return;

        // Every turn the server finishes ends with `done` or `error`: a stream that just stops was cut
        // (a proxy, the network), so it is not an answer, however much of one arrived.
        if (turn.stopRequested) onStopped(accumulatedContent);
        else onError(chatErrorMessage('failed'));
      } catch (error) {
        if (turn.stopRequested) {
          onStopped(accumulatedContent);
          return;
        }
        console.error('Streaming error:', error);
        // A TypeError is fetch failing to reach the server at all; the Errors thrown above carry
        // the app's own message for a refused request (the daily limit, signed out).
        onError(
          error instanceof TypeError
            ? chatErrorMessage('busy')
            : error instanceof Error
              ? error.message
              : chatErrorMessage('failed'),
        );
      } finally {
        if (turnRef.current === turn) turnRef.current = null;
        setStreamingStatus({ isLoading: false, currentEvent: null, statusText: '' });
      }
    },
    [dispatch, errorMessages, profile, pageContext, toolLabels],
  );

  return { sendStreamingMessage, stop, streamingStatus };
};
