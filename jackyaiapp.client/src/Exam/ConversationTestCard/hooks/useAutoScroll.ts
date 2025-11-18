import { useEffect, useRef } from 'react';

import { ConversationTurn } from '@/apis/examApis/types';

export function useAutoScroll(
  conversationHistory: ConversationTurn[] | undefined,
  isResponding: boolean,
) {
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const scrollToBottom = () => {
      messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    };

    if (conversationHistory) {
      // Small delay to ensure DOM is updated
      setTimeout(scrollToBottom, 100);
    }
  }, [conversationHistory, isResponding]);

  return messagesEndRef;
}
