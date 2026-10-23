import { useCallback, useEffect, useRef, useState } from 'react';

import { Message, QueuedMessage } from '../types';

import { chatbotApis, useLazyGetConversationMessagesQuery } from '@/apis/chatbotApis';
import { ChatAttachment, ChatHistoryMessage } from '@/apis/chatbotApis/types';
import { stoppedContent, useChatStreaming } from '@/hooks/useChatStreaming';
import { useStreamingHtmlParser } from '@/hooks/useStreamingHtmlParser';
import { useAppDispatch } from '@/redux/hooks';

/** Maps stored conversation rows (one per turn) into chat bubbles. */
export const mapHistoryToMessages = (items: ChatHistoryMessage[]): Message[] =>
  items.map((m) => ({
    id: String(m.id),
    serverId: m.id,
    content: m.content,
    isUser: m.role === 'user',
    timestamp: new Date(m.createdAt),
    attachments: m.attachments,
  }));

/**
 * The conversation on screen: its messages, the reply being written into the last of them, the
 * messages waiting for it, and switching to another conversation or a new one.
 */
export function useChatConversation() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [conversationId, setConversationId] = useState<string>('');
  const [currentStreamingContent, setCurrentStreamingContent] = useState<string>('');

  const dispatch = useAppDispatch();
  const [fetchConversationMessages] = useLazyGetConversationMessagesQuery();
  const { sendStreamingMessage, stop, streamingStatus } = useChatStreaming();
  const { blocks, currentHtmlBlock } = useStreamingHtmlParser(currentStreamingContent);
  const busy = streamingStatus.isLoading;
  const nextTurnKey = useRef(0);

  // Start a fresh conversation (the server assigns a new id with the next reply). Not while a
  // reply is still coming in: it would carry on into the old conversation under an empty screen.
  // Says whether it did.
  const newChat = useCallback(() => {
    if (busy) return false;
    setMessages([]);
    setConversationId('');
    setCurrentStreamingContent('');
    return true;
  }, [busy]);

  // Resume an existing conversation: load its stored history and make it active. Says whether it
  // did.
  const selectConversation = useCallback(
    async (id: string) => {
      if (busy) return false;
      try {
        const res = await fetchConversationMessages(id).unwrap();
        setMessages(mapHistoryToMessages(res.data));
        setConversationId(id);
        setCurrentStreamingContent('');
        return true;
      } catch (e) {
        console.error('Failed to load conversation messages:', e);
        return false;
      }
    },
    [fetchConversationMessages, busy],
  );

  // 實時更新最後一條 bot 消息的 parsedBlocks
  useEffect(() => {
    if (blocks.length > 0) {
      setMessages((prev) => {
        const lastMessage = prev[prev.length - 1];
        if (lastMessage && !lastMessage.isUser) {
          return prev.map((msg, index) =>
            index === prev.length - 1 ? { ...msg, parsedBlocks: blocks } : msg,
          );
        }
        return prev;
      });
    }
  }, [blocks]);

  /**
   * Sends one message and writes its answer into a new bubble as it comes. With `edited`, the
   * message replaces that earlier one: it and everything after it go (here, and on the server), and
   * its attachments go again with the new wording — the server re-attaches them itself.
   */
  const sendTurn = async (text: string, attachments: ChatAttachment[], edited?: Message) => {
    const userMessage: Message = {
      id: `${Date.now()}-${nextTurnKey.current++}`,
      content: text,
      isUser: true,
      timestamp: new Date(),
      attachments: attachments.length > 0 ? attachments : undefined,
    };
    // 創建初始的 bot 訊息
    const botMessage: Message = {
      id: `${Date.now()}-${nextTurnKey.current++}`,
      content: '',
      isUser: false,
      timestamp: new Date(),
      parsedBlocks: [],
    };
    setMessages((prev) => {
      const from = edited ? prev.findIndex((m) => m.id === edited.id) : -1;
      return [...(from >= 0 ? prev.slice(0, from) : prev), userMessage, botMessage];
    });
    setCurrentStreamingContent('');

    const update = (id: string, patch: Partial<Message>) =>
      setMessages((prev) => prev.map((msg) => (msg.id === id ? { ...msg, ...patch } : msg)));
    const updateBot = (patch: Partial<Message>) => update(botMessage.id, patch);

    await sendStreamingMessage({
      message: text,
      conversationId,
      attachmentIds: edited ? [] : attachments.map((a) => a.id),
      editMessageId: edited?.serverId,
      onStart: (id, userMessageId) => {
        setConversationId(id);
        if (userMessageId) update(userMessage.id, { serverId: userMessageId });
      },
      onUpdate: (content) => {
        setCurrentStreamingContent(content);
        updateBot({ content });
      },
      onComplete: (newConversationId, truncated, assistantMessageId) => {
        if (newConversationId) setConversationId(newConversationId);
        updateBot({ truncated: truncated || undefined, serverId: assistantMessageId });
      },
      // What was said before the stop stays, marked the way the server stores it. `parsedBlocks` is
      // cleared so the bubble renders `content`, mark included.
      onStopped: (partial, assistantMessageId) =>
        updateBot({
          content: stoppedContent(partial),
          parsedBlocks: undefined,
          serverId: assistantMessageId,
        }),
      // `parsedBlocks` is cleared so the bubble falls back to rendering `content`: an empty
      // block list is what a reply that never started looks like, and it draws nothing.
      onError: (error) => updateBot({ content: `錯誤: ${error}`, parsedBlocks: undefined }),
    });

    // The turn is over, however it ended: the first message of a new conversation has just
    // created it on the server, and any turn moves it to the top. The column shows the list.
    dispatch(chatbotApis.util.invalidateTags(['Conversations']));
  };

  // Messages sent while an answer is still being written wait here, and go out one at a time as
  // each answer ends — however it ends, a stop included. (Stopping with one waiting is how to
  // break in: the answer stops and the waiting message is sent straight away.)
  const [queued, setQueued] = useState<QueuedMessage[]>([]);
  useEffect(() => {
    if (busy || queued.length === 0) return;
    const [next, ...rest] = queued;
    setQueued(rest);
    void sendTurn(next.text, next.attachments);
    // sendTurn is a fresh function every render; the queue moves on busy and the queue alone.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [busy, queued]);

  /** Sends a message now, or, while an answer is being written, once it is done. */
  const send = (text: string, attachments: ChatAttachment[]) => {
    if (busy) {
      setQueued((prev) => [...prev, { key: `queued-${nextTurnKey.current++}`, text, attachments }]);
    } else {
      void sendTurn(text, attachments);
    }
  };

  /** Sends `text` in place of an earlier message; not while an answer is being written. */
  const edit = (message: Message, text: string) => {
    if (busy) return;
    void sendTurn(text, message.attachments ?? [], message);
  };

  const removeQueued = (key: string) => setQueued((prev) => prev.filter((q) => q.key !== key));

  return {
    messages,
    conversationId,
    blocks,
    currentHtmlBlock,
    streamingStatus,
    busy,
    stop,
    queued,
    send,
    edit,
    removeQueued,
    newChat,
    selectConversation,
  };
}
