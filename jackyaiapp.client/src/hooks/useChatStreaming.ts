import { useCallback, useState } from 'react';

import { getApiErrorMessage } from '@/apis/utils/errorMessage';
import { useAppDispatch } from '@/redux/hooks';
import { openUsageLimitModal } from '@/redux/uiSlice';

export interface StreamingStatus {
  isLoading: boolean;
  currentEvent: string | null;
  statusText: string;
}

/** Our own SSE event shape from ChatbotController — see AnthropicChatService.WriteSseAsync. */
type ChatStreamEvent =
  | { type: 'delta'; text: string }
  | { type: 'tool_use'; name: string }
  | { type: 'done'; conversationId: string }
  | { type: 'error'; message: string };

export const useChatStreaming = () => {
  const dispatch = useAppDispatch();
  const [streamingStatus, setStreamingStatus] = useState<StreamingStatus>({
    isLoading: false,
    currentEvent: null,
    statusText: '',
  });

  const sendStreamingMessage = useCallback(
    async (
      message: string,
      conversationId: string | undefined,
      onMessageUpdate: (content: string) => void,
      onComplete: (conversationId?: string) => void,
      onError: (error: string) => void,
    ) => {
      setStreamingStatus({ isLoading: true, currentEvent: null, statusText: '正在連接...' });

      try {
        const response = await fetch('/api/chatbot/chat', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Accept: 'text/event-stream',
            'Cache-Control': 'no-cache',
          },
          body: JSON.stringify({ message, conversationId }),
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
        let accumulatedContent = '';

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
                case 'delta':
                  accumulatedContent += event.text;
                  onMessageUpdate(accumulatedContent);
                  break;
                case 'tool_use':
                  setStreamingStatus((prev) => ({
                    ...prev,
                    currentEvent: 'tool_use',
                    statusText: `🔄 正在查詢 ${event.name}...`,
                  }));
                  break;
                case 'done':
                  onComplete(event.conversationId);
                  return;
                case 'error':
                  onError(event.message);
                  return;
              }
            } catch (e) {
              console.warn('Failed to parse SSE line:', line, e);
            }
          }
        }

        if (!accumulatedContent) onComplete(conversationId);
      } catch (error) {
        console.error('Streaming error:', error);
        onError(error instanceof Error ? error.message : 'Unknown error occurred');
      } finally {
        setStreamingStatus({ isLoading: false, currentEvent: null, statusText: '' });
      }
    },
    [dispatch],
  );

  return { sendStreamingMessage, streamingStatus };
};
