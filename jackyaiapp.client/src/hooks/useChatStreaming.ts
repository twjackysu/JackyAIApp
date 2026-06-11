import { useCallback, useState } from 'react';

import { handleDifyEvent } from './difyEventHandlers';

import { CREDIT_COSTS } from '@/constants/creditCosts';
import { useAppDispatch } from '@/redux/hooks';
import { openCreditModal } from '@/redux/uiSlice';

export interface StreamingStatus {
  isLoading: boolean;
  currentEvent: string | null;
  statusText: string;
}

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
        const token = localStorage.getItem('token');

        const response = await fetch('/api/chatbot/chat', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${token}`,
            Accept: 'text/event-stream',
            'Cache-Control': 'no-cache',
          },
          body: JSON.stringify({ message, conversationId }),
        });

        if (!response.ok) {
          const text = await response.text().catch(() => '');
          if (response.status === 402) {
            dispatch(
              openCreditModal({
                message: text || undefined,
                requiredCredits: CREDIT_COSTS.CHAT_MESSAGE,
              }),
            );
          }
          throw new Error(text || `HTTP error! status: ${response.status}`);
        }

        if (!response.body) throw new Error('No response body');

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let accumulatedContent = '';
        let extractedConversationId: string | undefined;

        setStreamingStatus((prev) => ({ ...prev, statusText: '正在處理請求...' }));

        // Build context object wiring handler callbacks to local state + callers.
        const ctx = {
          setStatus: (event: string, text: string) =>
            setStreamingStatus((prev) => ({ ...prev, currentEvent: event, statusText: text })),
          appendContent: (delta: string) => {
            accumulatedContent += delta;
            onMessageUpdate(accumulatedContent);
          },
          replaceContent: (text: string) => {
            if (!accumulatedContent) {
              accumulatedContent = text;
              onMessageUpdate(accumulatedContent);
            }
          },
          complete: (convId?: string) => onComplete(convId),
          fail: (error: string) => onError(error),
        };

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          const lines = decoder.decode(value, { stream: true }).split('\n');

          for (const line of lines) {
            if (!line.trim()) continue;
            try {
              if (!line.startsWith('data: ')) continue;
              const data = JSON.parse(line.slice(6));

              if (data.conversation_id && !extractedConversationId)
                extractedConversationId = data.conversation_id;

              const result = handleDifyEvent(data, ctx);

              if (result === 'error') return;

              if (result === 'complete') {
                setTimeout(() => onComplete(extractedConversationId), 500);
                return;
              }
            } catch (e) {
              console.warn('Failed to parse SSE line:', line, e);
            }
          }
        }

        if (!accumulatedContent) onComplete(extractedConversationId);
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
