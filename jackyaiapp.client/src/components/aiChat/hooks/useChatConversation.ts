import { useCallback, useEffect, useRef, useState } from 'react';

import { Message, QueuedMessage } from '../types';
import { ApprovalDecision, MessageApproval } from '../utils/approval';
import { applyToolEvent, settleToolSteps, ToolStep } from '../utils/toolActivity';

import { chatbotApis, useLazyGetConversationMessagesQuery } from '@/apis/chatbotApis';
import { ChatAttachment, ChatHistoryMessage } from '@/apis/chatbotApis/types';
import { ChatStreamingOptions, stoppedContent, useChatStreaming } from '@/hooks/useChatStreaming';
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
export function useChatConversation(options?: ChatStreamingOptions) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [conversationId, setConversationId] = useState<string>('');
  const [currentStreamingContent, setCurrentStreamingContent] = useState<string>('');

  const dispatch = useAppDispatch();
  const [fetchConversationMessages] = useLazyGetConversationMessagesQuery();
  const { sendStreamingMessage, stop, streamingStatus } = useChatStreaming(options);
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

  /** Changes the approval card waiting under that pendingId, wherever it is. */
  const updateApproval = (pendingId: string, patch: Partial<MessageApproval>) =>
    setMessages((prev) =>
      prev.map((msg) =>
        msg.approval?.request.pendingId === pendingId
          ? { ...msg, approval: { ...msg.approval, ...patch } }
          : msg,
      ),
    );

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
    // The tool steps change from their own last state, not from a copy taken when the turn began.
    const updateSteps = (next: (steps: ToolStep[]) => ToolStep[]) =>
      setMessages((prev) =>
        prev.map((msg) => {
          if (msg.id !== botMessage.id) return msg;
          const steps = next(msg.toolSteps ?? []);
          // An answer that used no tool keeps no list.
          return steps.length > 0 ? { ...msg, toolSteps: steps } : msg;
        }),
      );
    const settleSteps = (answered: boolean) =>
      updateSteps((steps) => settleToolSteps(steps, answered));

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
        settleSteps(true);
      },
      onToolEvent: (event) => updateSteps((steps) => applyToolEvent(steps, event)),
      onApprovalEvent: (event) =>
        event.type === 'approval_required'
          ? updateBot({
              approval: {
                request: { pendingId: event.pendingId, calls: event.calls, tainted: event.tainted },
                state: 'pending',
              },
            })
          : updateApproval(event.pendingId, { state: 'resolved', decisions: event.decisions }),
      // What was said before the stop stays, marked the way the server stores it. `parsedBlocks` is
      // cleared so the bubble renders `content`, mark included.
      onStopped: (partial, assistantMessageId) => {
        updateBot({
          content: stoppedContent(partial),
          parsedBlocks: undefined,
          serverId: assistantMessageId,
        });
        // The calls made before the stop did run; none is left spinning.
        settleSteps(true);
      },
      // `parsedBlocks` is cleared so the bubble falls back to rendering `content`: an empty
      // block list is what a reply that never started looks like, and it draws nothing.
      onError: (error) => {
        updateBot({ content: `錯誤: ${error}`, parsedBlocks: undefined });
        settleSteps(false);
      },
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

  /**
   * Sends the reader's decisions on a write the answer is waiting for. The answer carries on in its
   * own stream once the server has them (`approval_resolved` marks the card answered); one that did
   * not arrive can be sent again.
   */
  const decide = async (pendingId: string, decisions: ApprovalDecision[]) => {
    if (!conversationId) return;
    updateApproval(pendingId, { state: 'sending', decisions });
    try {
      const res = await fetch(
        `/api/chatbot/conversations/${encodeURIComponent(conversationId)}/approvals/${encodeURIComponent(pendingId)}`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ decisions }),
        },
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
    } catch {
      updateApproval(pendingId, { state: 'failed' });
    }
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
    decide,
    newChat,
    selectConversation,
  };
}
