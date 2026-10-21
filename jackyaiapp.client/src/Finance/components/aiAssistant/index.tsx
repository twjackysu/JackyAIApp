import Box from '@mui/material/Box';
import { alpha, useTheme } from '@mui/material/styles';
import { useCallback, useEffect, useRef, useState } from 'react';

import ChatHeader from './components/ChatHeader';
import ChatInput from './components/ChatInput';
import ConversationDrawer from './components/ConversationDrawer';
import ConversationList from './components/ConversationList';
import { HISTORY_COLUMN_BREAKPOINT, HISTORY_COLUMN_WIDTH } from './components/historyColumn';
import MessageArea from './components/MessageArea';
import PreviewDialog from './components/PreviewDialog';
import { useAgentStatus } from './hooks/useAgentStatus';
import { ACCENT, ACCENT_GRADIENT, hairline, panelSurface } from './style';
import { Message, PendingAttachment, QueuedMessage } from './types';
import { readPaste } from './utils/paste';

import {
  chatbotApis,
  useDeleteChatAttachmentMutation,
  useLazyGetConversationMessagesQuery,
  useUploadChatAttachmentMutation,
} from '@/apis/chatbotApis';
import {
  ATTACHMENT_ACCEPT,
  ATTACHMENT_ERROR_MESSAGES,
  attachmentErrorMessage,
  MAX_ATTACHMENT_BYTES,
  MAX_ATTACHMENTS,
} from '@/apis/chatbotApis/chatErrors';
import { ChatAttachment, ChatHistoryMessage } from '@/apis/chatbotApis/types';
import { stoppedContent, useChatStreaming } from '@/hooks/useChatStreaming';
import { ParsedBlock, useStreamingHtmlParser } from '@/hooks/useStreamingHtmlParser';
import { useAppDispatch } from '@/redux/hooks';

/** Maps stored conversation rows (one per turn) into chat bubbles. */
const mapHistoryToMessages = (items: ChatHistoryMessage[]): Message[] =>
  items.map((m) => ({
    id: String(m.id),
    content: m.content,
    isUser: m.role === 'user',
    timestamp: new Date(m.createdAt),
    attachments: m.attachments,
  }));

const ACCEPTED_EXTENSIONS = ATTACHMENT_ACCEPT.split(',');

/** Why a picked file will not be uploaded at all, or undefined when it can be. */
const rejectBeforeUpload = (file: File): string | undefined => {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!ACCEPTED_EXTENSIONS.includes(extension)) return ATTACHMENT_ERROR_MESSAGES.unsupported;
  if (file.size > MAX_ATTACHMENT_BYTES) return ATTACHMENT_ERROR_MESSAGES.tooLarge;
  if (file.size === 0) return ATTACHMENT_ERROR_MESSAGES.unreadable;
  return undefined;
};

const isImageFile = (file: File) => /\.(png|jpe?g|gif|webp)$/i.test(file.name);

// Object URLs made for previews and not yet revoked. Not every environment can make them (tests);
// a chip then shows its icon instead.
const livePreviewUrls = new Set<string>();
const createPreviewUrl = (file: File) => {
  if (typeof URL.createObjectURL !== 'function') return undefined;
  const url = URL.createObjectURL(file);
  livePreviewUrls.add(url);
  return url;
};
const revokePreviewUrl = (url: string | undefined) => {
  if (!url || !livePreviewUrls.delete(url)) return;
  URL.revokeObjectURL(url);
};

const KEEP_FORMATTING_KEY = 'aiAssistant.keepPasteFormatting';
const readKeepFormatting = () => {
  try {
    return localStorage.getItem(KEEP_FORMATTING_KEY) === '1';
  } catch {
    return false;
  }
};

/** What the folded one-line box shows about a conversation it is hiding. */
export interface AssistantActivity {
  /** A reply is being written. */
  busy: boolean;
  /** There is a conversation on screen (it is kept while folded). */
  hasConversation: boolean;
}

interface AiAssistantPanelProps {
  /** Whether the panel is open. It stays mounted while folded so a reply that is still streaming,
   *  and the conversation itself, survive folding it away. */
  active: boolean;
  /** The message being typed, shared with the one-line box so typing carries over either way. */
  inputValue: string;
  onInputChange: (value: string) => void;
  /** Fold the panel back into its one-line box. */
  onCollapse: () => void;
  onActivityChange?: (activity: AssistantActivity) => void;
}

/**
 * The Finance page's AI assistant as a block of the page (the chatbot used to float over it).
 * Its backend is Claude with a TW-stock MCP server attached, so it only answers stock questions
 * — which is why it lives on this page and nowhere else. The caller is responsible for only
 * rendering it for a signed-in user.
 */
function AiAssistantPanel({
  active,
  inputValue,
  onInputChange: setInputValue,
  onCollapse,
  onActivityChange,
}: AiAssistantPanelProps) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [conversationId, setConversationId] = useState<string>('');
  const [currentStreamingContent, setCurrentStreamingContent] = useState<string>('');

  // On-demand preview (HTML / Markdown) — opened from a button inside a message.
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewBlock, setPreviewBlock] = useState<ParsedBlock | null>(null);

  // The history is a fixed column beside the chat on wide screens. Where that would leave the chat
  // too narrow, it is a drawer that slides over this panel (not over the page) instead.
  const [historyOpen, setHistoryOpen] = useState(false);
  const [panelEl, setPanelEl] = useState<HTMLElement | null>(null);

  const dispatch = useAppDispatch();
  const [fetchConversationMessages] = useLazyGetConversationMessagesQuery();
  const { sendStreamingMessage, stop, streamingStatus } = useChatStreaming();
  const { statusText } = useAgentStatus(streamingStatus);
  const theme = useTheme();
  const { blocks, currentHtmlBlock } = useStreamingHtmlParser(currentStreamingContent);
  const busy = streamingStatus.isLoading;

  // Files picked for the next message. Each uploads as soon as it is picked, so the server has
  // read it by the time the message is sent.
  const [pendingAttachments, setPendingAttachments] = useState<PendingAttachment[]>([]);
  const [uploadAttachment] = useUploadChatAttachmentMutation();
  const [deleteAttachment] = useDeleteChatAttachmentMutation();
  const nextAttachmentKey = useRef(0);
  const nextTurnKey = useRef(0);
  // Chips removed while their upload was still running: the upload is deleted once it lands.
  const removedWhileUploading = useRef(new Set<string>());

  const updatePending = (key: string, patch: Partial<PendingAttachment>) =>
    setPendingAttachments((prev) => prev.map((a) => (a.key === key ? { ...a, ...patch } : a)));

  const handleAddFiles = (files: File[]) => {
    // A failed chip is only there to say why; it does not take up one of the slots.
    const taken = pendingAttachments.filter((a) => !a.error).length;
    const added: PendingAttachment[] = files.map((file, i) => {
      const key = `attachment-${nextAttachmentKey.current++}`;
      const error =
        taken + i >= MAX_ATTACHMENTS ? ATTACHMENT_ERROR_MESSAGES.tooMany : rejectBeforeUpload(file);
      const previewUrl = !error && isImageFile(file) ? createPreviewUrl(file) : undefined;
      return { key, fileName: file.name, error, previewUrl };
    });
    setPendingAttachments((prev) => [...prev, ...added]);

    added.forEach((pending, i) => {
      if (pending.error) return;
      uploadAttachment(files[i])
        .unwrap()
        .then((res) => {
          if (removedWhileUploading.current.delete(pending.key)) {
            deleteAttachment(res.data.id);
          } else {
            updatePending(pending.key, { attachment: res.data });
          }
        })
        .catch((e: unknown) => {
          removedWhileUploading.current.delete(pending.key);
          revokePreviewUrl(pending.previewUrl);
          updatePending(pending.key, { error: attachmentErrorMessage(e), previewUrl: undefined });
        });
    });
  };

  const handleRemoveAttachment = (key: string) => {
    const pending = pendingAttachments.find((a) => a.key === key);
    if (!pending) return;
    if (pending.attachment) deleteAttachment(pending.attachment.id);
    else if (!pending.error) removedWhileUploading.current.add(key);
    revokePreviewUrl(pending.previewUrl);
    setPendingAttachments((prev) => prev.filter((a) => a.key !== key));
  };

  // The local previews still held are let go when the panel goes away, too.
  useEffect(() => () => livePreviewUrls.forEach(revokePreviewUrl), []);

  // Whether pasted formatted text keeps its formatting (as Markdown); remembered in this browser.
  const [keepFormatting, setKeepFormatting] = useState(readKeepFormatting);
  const handleKeepFormattingChange = (keep: boolean) => {
    setKeepFormatting(keep);
    try {
      localStorage.setItem(KEEP_FORMATTING_KEY, keep ? '1' : '0');
    } catch {
      // Storage unavailable (private window, blocked site data): the choice lasts this visit only.
    }
  };

  const handlePaste = (e: React.ClipboardEvent) => {
    const result = readPaste(e.clipboardData, keepFormatting);
    if (result.kind === 'default') return;
    e.preventDefault();
    if (result.kind === 'files') {
      handleAddFiles(result.files);
      return;
    }
    // Put the converted text where the browser would have put the pasted text. A block (a table,
    // a list) goes on lines of its own: typed on straight after it, a question would become part
    // of the table's last row.
    const input = inputRef.current;
    const start = input?.selectionStart ?? inputValue.length;
    const end = input?.selectionEnd ?? inputValue.length;
    const before = inputValue.slice(0, start);
    const block = result.text.includes('\n');
    const text =
      (block && before && !before.endsWith('\n') ? '\n' : '') + result.text + (block ? '\n' : '');
    setInputValue(before + text + inputValue.slice(end));
    const caret = start + text.length;
    requestAnimationFrame(() => inputRef.current?.setSelectionRange(caret, caret));
  };

  // Opening the panel is the reader asking to type: the cursor goes to the end of the message
  // they started in the one-line box. Also once a reply is done, since the box is disabled (and
  // so loses focus) while it is written.
  const inputRef = useRef<HTMLTextAreaElement>(null);
  useEffect(() => {
    const input = inputRef.current;
    if (!active || busy || !input) return;
    input.focus();
    input.setSelectionRange(input.value.length, input.value.length);
  }, [active, busy]);

  const hasConversation = messages.length > 0;
  useEffect(() => {
    onActivityChange?.({ busy, hasConversation });
  }, [busy, hasConversation, onActivityChange]);

  const handlePreview = (block: ParsedBlock) => {
    setPreviewBlock(block);
    setPreviewOpen(true);
  };

  const handleClosePreview = () => setPreviewOpen(false);

  // Start a fresh conversation (the server assigns a new id with the next reply). Not while a
  // reply is still coming in: it would carry on into the old conversation under an empty screen.
  const handleNewChat = useCallback(() => {
    if (busy) return;
    setMessages([]);
    setConversationId('');
    setCurrentStreamingContent('');
    setPreviewOpen(false);
  }, [busy]);

  // Resume an existing conversation: load its stored history and make it active.
  const handleSelectConversation = useCallback(
    async (id: string) => {
      if (busy) return;
      try {
        const res = await fetchConversationMessages(id).unwrap();
        setMessages(mapHistoryToMessages(res.data));
        setConversationId(id);
        setCurrentStreamingContent('');
        setPreviewOpen(false);
      } catch (e) {
        console.error('Failed to load conversation messages:', e);
      }
    },
    [fetchConversationMessages, busy],
  );

  // While a preview is open on a still-streaming HTML block, keep it in sync with
  // the latest parsed content (guarded by content/completeness to avoid render loops).
  useEffect(() => {
    if (!previewOpen || !previewBlock || previewBlock.isComplete) return;
    const live = currentHtmlBlock ?? blocks.find((b) => b.contentType === 'html');
    if (
      live &&
      (live.content !== previewBlock.content || live.isComplete !== previewBlock.isComplete)
    ) {
      setPreviewBlock(live);
    }
  }, [currentHtmlBlock, blocks, previewOpen, previewBlock]);

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

  /** Sends one message and writes its answer into a new bubble as it comes. */
  const sendTurn = async (text: string, attachments: ChatAttachment[]) => {
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
    setMessages((prev) => [...prev, userMessage, botMessage]);
    setCurrentStreamingContent('');

    const updateBot = (patch: Partial<Message>) =>
      setMessages((prev) =>
        prev.map((msg) => (msg.id === botMessage.id ? { ...msg, ...patch } : msg)),
      );

    await sendStreamingMessage({
      message: text,
      conversationId,
      attachmentIds: attachments.map((a) => a.id),
      onStart: setConversationId,
      onUpdate: (content) => {
        setCurrentStreamingContent(content);
        updateBot({ content });
      },
      onComplete: (newConversationId, truncated) => {
        if (newConversationId) setConversationId(newConversationId);
        if (truncated) updateBot({ truncated: true });
      },
      // What was said before the stop stays, marked the way the server stores it. `parsedBlocks` is
      // cleared so the bubble renders `content`, mark included.
      onStopped: (partial) =>
        updateBot({ content: stoppedContent(partial), parsedBlocks: undefined }),
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

  const handleSendMessage = () => {
    if (!inputValue.trim()) return;
    if (pendingAttachments.some((a) => !a.attachment && !a.error)) return; // still uploading

    const text = inputValue;
    const attachments = pendingAttachments.flatMap((a) => (a.attachment ? [a.attachment] : []));
    setInputValue('');
    // The sent message shows its images from the server from here on.
    pendingAttachments.forEach((a) => revokePreviewUrl(a.previewUrl));
    setPendingAttachments([]);

    if (busy) {
      setQueued((prev) => [...prev, { key: `queued-${nextTurnKey.current++}`, text, attachments }]);
    } else {
      void sendTurn(text, attachments);
    }
  };

  const handleRemoveQueued = (key: string) =>
    setQueued((prev) => prev.filter((q) => q.key !== key));

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSendMessage();
    }
  };

  return (
    <>
      <Box
        ref={setPanelEl}
        component="section"
        aria-label="AI 助手"
        sx={{
          // `relative` + `overflow: hidden` keep the history drawer inside the panel.
          position: 'relative',
          display: 'flex',
          overflow: 'hidden',
          height: 'clamp(460px, 74dvh, 780px)',
          borderRadius: '20px',
          // Its own surface, outlined in the accent gradient with a faint glow from the top
          // corners, so it does not read as one more of the market-data cards below it.
          border: '1px solid transparent',
          background: [
            `radial-gradient(520px circle at 0% 0%, ${alpha(ACCENT, 0.07)}, transparent 60%) padding-box`,
            `radial-gradient(520px circle at 100% 0%, ${alpha('#818cf8', 0.06)}, transparent 60%) padding-box`,
            `linear-gradient(${panelSurface(theme)}, ${panelSurface(theme)}) padding-box`,
            `${ACCENT_GRADIENT} border-box`,
          ].join(', '),
          boxShadow: '0 30px 80px -48px rgba(52, 211, 153, 0.55)',
        }}
      >
        {/* Conversation history, always in view where there is room for it */}
        <Box
          sx={{
            display: { xs: 'none', [HISTORY_COLUMN_BREAKPOINT]: 'block' },
            width: HISTORY_COLUMN_WIDTH,
            flexShrink: 0,
            borderRight: `1px solid ${hairline(theme)}`,
            bgcolor: theme.palette.mode === 'dark' ? 'rgba(0,0,0,0.22)' : 'rgba(0,0,0,0.02)',
          }}
        >
          <ConversationList
            activeConversationId={conversationId}
            onSelect={handleSelectConversation}
            onNewChat={handleNewChat}
            busy={busy}
          />
        </Box>

        <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
          <ChatHeader
            statusText={statusText}
            busy={busy}
            onOpenHistory={() => setHistoryOpen(true)}
            onNewChat={handleNewChat}
            onCollapse={onCollapse}
          />
          <MessageArea
            messages={messages}
            onPreview={handlePreview}
            active={active}
            onSuggest={(question) => {
              setInputValue(question);
              inputRef.current?.focus();
            }}
          />
          <ChatInput
            value={inputValue}
            onChange={setInputValue}
            onSend={handleSendMessage}
            onKeyPress={handleKeyPress}
            busy={busy}
            onStop={stop}
            queued={queued}
            onRemoveQueued={handleRemoveQueued}
            inputRef={inputRef}
            attachments={pendingAttachments}
            onAddFiles={handleAddFiles}
            onRemoveAttachment={handleRemoveAttachment}
            onPaste={handlePaste}
            keepFormatting={keepFormatting}
            onKeepFormattingChange={handleKeepFormattingChange}
          />
        </Box>

        <ConversationDrawer
          open={historyOpen}
          onClose={() => setHistoryOpen(false)}
          activeConversationId={conversationId}
          onSelect={handleSelectConversation}
          onNewChat={handleNewChat}
          busy={busy}
          containerEl={panelEl}
        />
      </Box>

      <PreviewDialog open={previewOpen} onClose={handleClosePreview} block={previewBlock} />
    </>
  );
}

export default AiAssistantPanel;
