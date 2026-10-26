import { useCallback, useEffect, useRef, useState } from 'react';

import { parseToolCard, sitePathOf, type ToolCard } from '../cards/toolCard';
import { Message, QueuedMessage } from '../types';
import {
  alwaysAllowRequest,
  ApprovalCall,
  ApprovalDecision,
  MessageApproval,
  resolveApproval,
} from '../utils/approval';
import { handoffOf } from '../utils/handoff';
import { addThinking, pauseThinking, type ThinkingTrace } from '../utils/thinking';
import { applyToolEvent, settleToolSteps, ToolStep } from '../utils/toolActivity';

import { chatbotApis, useLazyGetConversationMessagesQuery } from '@/apis/chatbotApis';
import { ChatAttachment, ChatHistoryMessage } from '@/apis/chatbotApis/types';
import { connectorsApi } from '@/apis/connectorsApis/connectorsApis';
import { ChatStreamingOptions, stoppedContent, useChatStreaming } from '@/hooks/useChatStreaming';
import { useStreamingHtmlParser } from '@/hooks/useStreamingHtmlParser';
import { useAppDispatch } from '@/redux/hooks';

/** How a message goes. */
export interface SendOptions {
  /**
   * As the first message of a new conversation rather than into the one on screen (the home page's
   * briefing): the old one's whole history is not sent again, and what it read does not make this
   * one's writes ask first. While an answer is being written it waits like any message, then starts
   * the new conversation.
   */
  fresh?: boolean;
  /**
   * The message quotes text the reader selected on a page (Studio AI's selection bar), which may be
   * someone else's words: the server treats the conversation as having read outside content.
   */
  quotesPageText?: boolean;
  /**
   * The message is a task another assistant handed over (Studio AI's handoff, D-095): its name, shown over the
   * reader's message so they see where it came from (D-099). Nothing of it goes to the server.
   */
  handedOverBy?: string;
  /**
   * Read the answer aloud once it is done (D-120): the reader asked by voice, or for a briefing to listen to. Only in
   * this session and nothing of it goes to the server; reopening the conversation reads nothing.
   */
  readAloud?: boolean;
}

/** Maps stored conversation rows (one per turn) into chat bubbles. */
export const mapHistoryToMessages = (items: ChatHistoryMessage[]): Message[] =>
  items.map((m) => ({
    id: String(m.id),
    serverId: m.id,
    content: m.content,
    isUser: m.role === 'user',
    timestamp: new Date(m.createdAt),
    attachments: m.attachments,
    ...storedCards(m.cards),
  }));

/** The cards stored with an answer, each checked as a streamed one is (`parseToolCard`). */
const storedCards = (raw: unknown): Pick<Message, 'cards'> => {
  if (!Array.isArray(raw)) return {};
  const cards = raw.map(parseToolCard).filter((c): c is ToolCard => c !== null);
  return cards.length > 0 ? { cards } : {};
};

/**
 * The conversation on screen: its messages, the reply being written into the last of them, the
 * messages waiting for it, and switching to another conversation or a new one.
 */
export interface ChatConversationOptions extends ChatStreamingOptions {
  /** A tool result moved the reader's page (`tool_result.navigate`, a path on this site); only as it streams,
   *  never from a reopened conversation. Without it the result's link card is all the reader gets. `handoff` is a
   *  question for that page's own assistant (`tool_result.handoff`, `utils/handoff`). */
  onNavigate?: (path: string, handoff?: string) => void;
  /** One of the profile's own tools finished without failing (`tool_result` with `ok`), named as the server names
   *  it; only as it streams. Lets the page read again what a write changed (Studio AI, `refreshAfterTool`). */
  onToolDone?: (tool: string) => void;
}

export function useChatConversation(options?: ChatConversationOptions) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [conversationId, setConversationId] = useState<string>('');
  const [currentStreamingContent, setCurrentStreamingContent] = useState<string>('');

  const dispatch = useAppDispatch();
  const [fetchConversationMessages] = useLazyGetConversationMessagesQuery();
  const { sendStreamingMessage, stop, streamingStatus } = useChatStreaming(options);
  // The latest handler, whenever the result arrives (a turn's callbacks are the render's that sent it).
  const onNavigateRef = useRef(options?.onNavigate);
  const onToolDoneRef = useRef(options?.onToolDone);
  useEffect(() => {
    onNavigateRef.current = options?.onNavigate;
    onToolDoneRef.current = options?.onToolDone;
  });
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
  const sendTurn = async (
    text: string,
    attachments: ChatAttachment[],
    edited?: Message,
    { fresh = false, quotesPageText = false, handedOverBy, readAloud = false }: SendOptions = {},
  ) => {
    const userMessage: Message = {
      id: `${Date.now()}-${nextTurnKey.current++}`,
      content: text,
      isUser: true,
      timestamp: new Date(),
      attachments: attachments.length > 0 ? attachments : undefined,
      ...(handedOverBy && { handedOverBy }),
      ...(quotesPageText && { quotesPageText }),
      ...(edited?.serverId && { replacesServerId: edited.serverId }),
    };
    // 創建初始的 bot 訊息
    const botMessage: Message = {
      id: `${Date.now()}-${nextTurnKey.current++}`,
      content: '',
      isUser: false,
      timestamp: new Date(),
      parsedBlocks: [],
      ...(readAloud && { readAloud }),
    };
    setMessages((prev) => {
      if (fresh) return [userMessage, botMessage];
      const from = edited ? prev.findIndex((m) => m.id === edited.id) : -1;
      return [...(from >= 0 ? prev.slice(0, from) : prev), userMessage, botMessage];
    });
    setCurrentStreamingContent('');
    // This render's conversationId is the old one: a fresh message names none, so the server starts one.
    if (fresh) setConversationId('');

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
    // The model's thinking (utils/thinking): grows while it streams, and each other event ends the span going on.
    const updateThinking = (
      next: (trace: ThinkingTrace | undefined) => ThinkingTrace | undefined,
    ) =>
      setMessages((prev) => {
        const msg = prev.find((m) => m.id === botMessage.id);
        if (!msg) return prev;
        const trace = next(msg.thinking);
        // Nothing to change (not thinking): the same list, so no render.
        if (trace === msg.thinking) return prev;
        return prev.map((m) => (m === msg ? { ...m, thinking: trace } : m));
      });
    const pause = () => updateThinking((trace) => pauseThinking(trace, Date.now()));
    // A question left open when the turn ended was never answered: the server ran none of it, and
    // a click now would find nothing waiting. It shows as closed with nothing run (no decisions).
    const closeApproval = () =>
      setMessages((prev) =>
        prev.map((msg) =>
          msg.id === botMessage.id && msg.approval && msg.approval.state !== 'resolved'
            ? { ...msg, approval: { ...msg.approval, state: 'resolved', decisions: [] } }
            : msg,
        ),
      );

    await sendStreamingMessage({
      message: text,
      conversationId: fresh ? '' : conversationId,
      // A stored message's files are re-attached by the server; one it never stored (asked again, D-139) sends them.
      attachmentIds: edited?.serverId ? [] : attachments.map((a) => a.id),
      editMessageId: edited?.serverId,
      quotesPageText,
      onStart: (id, userMessageId) => {
        setConversationId(id);
        if (userMessageId) update(userMessage.id, { serverId: userMessageId });
      },
      onUpdate: (content) => {
        pause();
        setCurrentStreamingContent(content);
        updateBot({ content });
      },
      onThinking: (piece) => updateThinking((trace) => addThinking(trace, piece, Date.now())),
      onComplete: (newConversationId, truncated, assistantMessageId) => {
        pause();
        if (newConversationId) setConversationId(newConversationId);
        updateBot({
          truncated: truncated || undefined,
          serverId: assistantMessageId,
          answered: true,
        });
        settleSteps(true);
        closeApproval();
      },
      onToolEvent: (event) => {
        pause();
        updateSteps((steps) => applyToolEvent(steps, event));
        if (event.type === 'tool_result' && event.ok) {
          const path = sitePathOf(event.navigate);
          if (path) onNavigateRef.current?.(path, handoffOf(event.handoff));
        }
        if (event.type === 'tool_result' && event.ok) onToolDoneRef.current?.(event.name);
      },
      onApprovalEvent: (event) => {
        pause();
        if (event.type === 'approval_required')
          updateBot({
            approval: {
              request: {
                pendingId: event.pendingId,
                calls: event.calls,
                tainted: event.tainted,
                askedAt: Date.now(),
              },
              state: 'pending',
            },
          });
        else
          setMessages((prev) =>
            prev.map((msg) =>
              msg.approval?.request.pendingId === event.pendingId
                ? { ...msg, approval: resolveApproval(msg.approval, event.decisions) }
                : msg,
            ),
          );
      },
      // What was said before the stop stays, marked the way the server stores it. `parsedBlocks` is
      // cleared so the bubble renders `content`, mark included.
      onStopped: (partial, assistantMessageId) => {
        pause();
        updateBot({
          content: stoppedContent(partial),
          parsedBlocks: undefined,
          serverId: assistantMessageId,
        });
        // The calls made before the stop did run; none is left spinning.
        settleSteps(true);
        closeApproval();
      },
      // `parsedBlocks` is cleared so the bubble falls back to rendering `content`: an empty
      // block list is what a reply that never started looks like, and it draws nothing.
      // A failed turn shows only its error, as the server keeps nothing of it: its thinking goes too.
      onError: (error, retryable) => {
        setMessages((prev) =>
          prev.map((msg) =>
            msg.id === botMessage.id
              ? {
                  ...msg,
                  content: `錯誤: ${error}`,
                  parsedBlocks: undefined,
                  thinking: undefined,
                  // Not after one of the site's or a connector's tools ran (they carry an id; tw-stock's hosted
                  // ones do not): a failed turn is not stored, so asked again the model would not know a write
                  // already happened, and could make it twice (D-139).
                  ...(retryable &&
                    !msg.toolSteps?.some((step) => step.id && step.status === 'done') && {
                      retryable,
                    }),
                }
              : msg,
          ),
        );
        settleSteps(false);
        closeApproval();
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
    void sendTurn(next.text, next.attachments, undefined, next);
    // sendTurn is a fresh function every render; the queue moves on busy and the queue alone.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [busy, queued]);

  /** Sends a message now, or, while an answer is being written, once it is done. */
  const send = (text: string, attachments: ChatAttachment[], options?: SendOptions) => {
    const fresh = options?.fresh === true;
    const quotesPageText = options?.quotesPageText === true;
    const handedOverBy = options?.handedOverBy;
    const readAloud = options?.readAloud === true;
    if (busy) {
      setQueued((prev) => [
        ...prev,
        {
          key: `queued-${nextTurnKey.current++}`,
          text,
          attachments,
          ...(fresh && { fresh }),
          ...(quotesPageText && { quotesPageText }),
          ...(handedOverBy && { handedOverBy }),
          ...(readAloud && { readAloud }),
        },
      ]);
    } else {
      void sendTurn(text, attachments, undefined, {
        fresh,
        quotesPageText,
        handedOverBy,
        readAloud,
      });
    }
  };

  /** Sends `text` in place of an earlier message; not while an answer is being written. */
  const edit = (message: Message, text: string) => {
    if (busy) return;
    void sendTurn(text, message.attachments ?? [], message);
  };

  /**
   * Asks again the question a failed answer was for (D-139), in its place: a question the server stored goes as an
   * edit of itself (it re-attaches the files), one it never got (the request failed before the turn started) is sent
   * anew, its files and its quoting of page text with it. Only the last answer, and not while one is written.
   */
  const retry = (answer: Message) => {
    if (busy) return;
    const index = messages.findIndex((m) => m.id === answer.id);
    const question = messages[index - 1];
    if (index !== messages.length - 1 || !question?.isUser) return;
    // An edit the server never got is still that edit: of the message it was to replace.
    const target = { ...question, serverId: question.serverId ?? question.replacesServerId };
    void sendTurn(question.content, question.attachments ?? [], target, {
      quotesPageText: question.quotesPageText === true,
      handedOverBy: question.handedOverBy,
      readAloud: answer.readAloud === true,
    });
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
      // Only a card still being sent: the server's own answer (its time ran out first) may already have closed it.
      setMessages((prev) =>
        prev.map((msg) =>
          msg.approval?.request.pendingId === pendingId && msg.approval.state === 'sending'
            ? { ...msg, approval: { ...msg.approval, state: 'failed' } }
            : msg,
        ),
      );
    }
  };

  /** Lets the tools of these allowed calls run without asking from now on, as the connectors page would. */
  const allowAlways = (calls: ApprovalCall[]) => {
    for (const call of calls) {
      void dispatch(connectorsApi.endpoints.setToolPermission.initiate(alwaysAllowRequest(call)));
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
    retry,
    removeQueued,
    decide,
    allowAlways,
    newChat,
    selectConversation,
  };
}
