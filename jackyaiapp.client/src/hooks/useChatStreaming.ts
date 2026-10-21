import { useCallback, useRef, useState } from 'react';

import { chatErrorMessage } from '@/apis/chatbotApis/chatErrors';
import { getApiErrorMessage } from '@/apis/utils/errorMessage';
import { useAppDispatch } from '@/redux/hooks';
import { openUsageLimitModal } from '@/redux/uiSlice';

export interface StreamingStatus {
  isLoading: boolean;
  currentEvent: string | null;
  statusText: string;
}

/** Our own SSE event shape from ChatbotController — see AnthropicChatService.WriteSseAsync. A
 *  failure carries only a code; `chatErrorMessage` turns it into words. */
type ChatStreamEvent =
  | { type: 'start'; conversationId: string }
  | { type: 'delta'; text: string }
  | { type: 'tool_use'; name: string }
  | { type: 'done'; conversationId: string; truncated?: boolean; stopped?: boolean }
  | { type: 'error'; code?: string };

export interface ChatTurn {
  message: string;
  conversationId: string | undefined;
  /** Uploaded files (useUploadChatAttachmentMutation) to send with this message. */
  attachmentIds: number[];
  /** The conversation the turn is in — known before any answer, so even a new one can be stopped. */
  onStart?: (conversationId: string) => void;
  /** The answer so far, each time it grows. */
  onUpdate: (content: string) => void;
  /** `truncated`: the answer ran into the length limit and is cut short. */
  onComplete: (conversationId?: string, truncated?: boolean) => void;
  /** The reader stopped the turn; `partial` is what had been written by then (the server keeps it). */
  onStopped: (partial: string) => void;
  onError: (error: string) => void;
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

export const useChatStreaming = () => {
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

  const sendStreamingMessage = useCallback(
    async ({
      message,
      conversationId,
      attachmentIds,
      onStart,
      onUpdate,
      onComplete,
      onStopped,
      onError,
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
          body: JSON.stringify({ message, conversationId, attachmentIds }),
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

        setStreamingStatus((prev) => ({ ...prev, statusText: '正在生成回應...' }));

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          const lines = decoder.decode(value, { stream: true }).split('\n');

          for (const line of lines) {
            if (!line.trim() || !line.startsWith('data: ')) continue;
            try {
              const event = JSON.parse(line.slice(6)) as ChatStreamEvent;

              switch (event.type) {
                case 'start':
                  turn.conversationId = event.conversationId;
                  onStart?.(event.conversationId);
                  // Stop was pressed before the server had named the conversation.
                  if (turn.stopRequested) {
                    void stopOnServer(event.conversationId);
                    turn.abort.abort();
                  }
                  break;
                case 'delta':
                  accumulatedContent += event.text;
                  onUpdate(accumulatedContent);
                  break;
                case 'tool_use':
                  setStreamingStatus((prev) => ({
                    ...prev,
                    currentEvent: 'tool_use',
                    statusText: `🔄 正在查詢 ${event.name}...`,
                  }));
                  break;
                case 'done':
                  if (event.stopped) onStopped(accumulatedContent);
                  else onComplete(event.conversationId, event.truncated === true);
                  return;
                case 'error':
                  onError(chatErrorMessage(event.code));
                  return;
              }
            } catch (e) {
              console.warn('Failed to parse SSE line:', line, e);
            }
          }
        }

        if (!accumulatedContent) onComplete(conversationId);
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
    [dispatch],
  );

  return { sendStreamingMessage, stop, streamingStatus };
};
