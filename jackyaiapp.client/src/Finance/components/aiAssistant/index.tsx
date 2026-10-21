import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import { useCallback, useEffect, useRef, useState } from 'react';

import ChatHeader from './components/ChatHeader';
import ChatInput from './components/ChatInput';
import ConversationDrawer from './components/ConversationDrawer';
import ConversationList from './components/ConversationList';
import { HISTORY_COLUMN_BREAKPOINT, HISTORY_COLUMN_WIDTH } from './components/historyColumn';
import MessageArea from './components/MessageArea';
import PreviewDialog from './components/PreviewDialog';
import ProgressIndicator from './components/ProgressIndicator';
import { useAgentStatus } from './hooks/useAgentStatus';
import { Message } from './types';

import { chatbotApis, useLazyGetConversationMessagesQuery } from '@/apis/chatbotApis';
import { ChatHistoryMessage } from '@/apis/chatbotApis/types';
import { useChatStreaming } from '@/hooks/useChatStreaming';
import { ParsedBlock, useStreamingHtmlParser } from '@/hooks/useStreamingHtmlParser';
import { useAppDispatch } from '@/redux/hooks';

/** Maps stored conversation rows (one per turn) into chat bubbles. */
const mapHistoryToMessages = (items: ChatHistoryMessage[]): Message[] =>
  items.map((m) => ({
    id: String(m.id),
    content: m.content,
    isUser: m.role === 'user',
    timestamp: new Date(m.createdAt),
  }));

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
  const { sendStreamingMessage, streamingStatus } = useChatStreaming();
  const { statusText, statusColor } = useAgentStatus(streamingStatus);
  const { blocks, currentHtmlBlock } = useStreamingHtmlParser(currentStreamingContent);
  const busy = streamingStatus.isLoading;

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

  const handleSendMessage = async () => {
    if (!inputValue.trim() || streamingStatus.isLoading) return;

    const userMessage: Message = {
      id: Date.now().toString(),
      content: inputValue,
      isUser: true,
      timestamp: new Date(),
    };

    setMessages((prev) => [...prev, userMessage]);
    setInputValue('');

    // 創建初始的 bot 訊息
    const botMessage: Message = {
      id: (Date.now() + 1).toString(),
      content: '',
      isUser: false,
      timestamp: new Date(),
      parsedBlocks: [],
    };

    setMessages((prev) => [...prev, botMessage]);

    await sendStreamingMessage(
      userMessage.content,
      conversationId,
      // onMessageUpdate
      (content: string) => {
        setCurrentStreamingContent(content);
        setMessages((prev) =>
          prev.map((msg) => (msg.id === botMessage.id ? { ...msg, content } : msg)),
        );
      },
      // onComplete
      (newConversationId?: string, truncated?: boolean) => {
        // 更新 conversation_id
        if (newConversationId) {
          setConversationId(newConversationId);
        }
        if (truncated) {
          setMessages((prev) =>
            prev.map((msg) => (msg.id === botMessage.id ? { ...msg, truncated: true } : msg)),
          );
        }
      },
      // onError
      (error: string) => {
        // `parsedBlocks` is cleared so the bubble falls back to rendering `content`: an empty
        // block list is what a reply that never started looks like, and it draws nothing.
        setMessages((prev) =>
          prev.map((msg) =>
            msg.id === botMessage.id
              ? { ...msg, content: `錯誤: ${error}`, parsedBlocks: undefined }
              : msg,
          ),
        );
      },
    );

    // The turn is over, however it ended: the first message of a new conversation has just
    // created it on the server, and any turn moves it to the top. The column shows the list.
    dispatch(chatbotApis.util.invalidateTags(['Conversations']));
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSendMessage();
    }
  };

  return (
    <>
      <Paper
        ref={setPanelEl}
        variant="outlined"
        sx={{
          // `relative` + `overflow: hidden` keep the history drawer inside the panel.
          position: 'relative',
          display: 'flex',
          overflow: 'hidden',
          height: 'clamp(420px, 72dvh, 760px)',
          borderRadius: 2,
          bgcolor: 'background.default',
        }}
      >
        {/* Conversation history, always in view where there is room for it */}
        <Box
          sx={{
            display: { xs: 'none', [HISTORY_COLUMN_BREAKPOINT]: 'block' },
            width: HISTORY_COLUMN_WIDTH,
            flexShrink: 0,
            borderRight: '1px solid',
            borderColor: 'divider',
            bgcolor: 'background.paper',
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
            statusColor={statusColor}
            busy={busy}
            onOpenHistory={() => setHistoryOpen(true)}
            onNewChat={handleNewChat}
            onCollapse={onCollapse}
          />
          <ProgressIndicator streamingStatus={streamingStatus} />
          <MessageArea messages={messages} onPreview={handlePreview} active={active} />
          <ChatInput
            value={inputValue}
            onChange={setInputValue}
            onSend={handleSendMessage}
            onKeyPress={handleKeyPress}
            disabled={busy}
            inputRef={inputRef}
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
      </Paper>

      <PreviewDialog open={previewOpen} onClose={handleClosePreview} block={previewBlock} />
    </>
  );
}

export default AiAssistantPanel;
