import Paper from '@mui/material/Paper';
import { useCallback, useEffect, useState } from 'react';

import ChatHeader from './components/ChatHeader';
import ChatInput from './components/ChatInput';
import ConversationDrawer from './components/ConversationDrawer';
import MessageArea from './components/MessageArea';
import PreviewDialog from './components/PreviewDialog';
import ProgressIndicator from './components/ProgressIndicator';
import { useAgentStatus } from './hooks/useAgentStatus';
import { Message } from './types';

import { useLazyGetConversationMessagesQuery } from '@/apis/chatbotApis';
import { ChatHistoryMessage } from '@/apis/chatbotApis/types';
import { useChatStreaming } from '@/hooks/useChatStreaming';
import { ParsedBlock, useStreamingHtmlParser } from '@/hooks/useStreamingHtmlParser';

/** Maps stored conversation rows (one per turn) into chat bubbles. */
const mapHistoryToMessages = (items: ChatHistoryMessage[]): Message[] =>
  items.map((m) => ({
    id: String(m.id),
    content: m.content,
    isUser: m.role === 'user',
    timestamp: new Date(m.createdAt),
  }));

interface AiAssistantPanelProps {
  /** Whether the panel's tab is showing. The panel stays mounted while hidden so a reply that is
   *  still streaming, and the conversation itself, survive switching to another tab. */
  active: boolean;
}

/**
 * The Finance page's AI assistant, laid out as a block of the page (the chatbot used to float
 * over it). Its backend is Claude with a TW-stock MCP server attached, so it only answers stock
 * questions — which is why it lives on this page and nowhere else. The caller is responsible for
 * only rendering it for a signed-in user.
 */
function AiAssistantPanel({ active }: AiAssistantPanelProps) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputValue, setInputValue] = useState('');
  const [conversationId, setConversationId] = useState<string>('');
  const [currentStreamingContent, setCurrentStreamingContent] = useState<string>('');

  // On-demand preview (HTML / Markdown) — opened from a button inside a message.
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewBlock, setPreviewBlock] = useState<ParsedBlock | null>(null);

  // Conversation history drawer, which slides over this panel (not over the page).
  const [historyOpen, setHistoryOpen] = useState(false);
  const [panelEl, setPanelEl] = useState<HTMLElement | null>(null);

  const [fetchConversationMessages] = useLazyGetConversationMessagesQuery();
  const { sendStreamingMessage, streamingStatus } = useChatStreaming();
  const { statusText, statusColor } = useAgentStatus(streamingStatus);
  const { blocks, currentHtmlBlock } = useStreamingHtmlParser(currentStreamingContent);

  const handlePreview = (block: ParsedBlock) => {
    setPreviewBlock(block);
    setPreviewOpen(true);
  };

  const handleClosePreview = () => setPreviewOpen(false);

  // Start a fresh conversation (the server assigns a new id with the next reply).
  const handleNewChat = useCallback(() => {
    setMessages([]);
    setConversationId('');
    setCurrentStreamingContent('');
    setPreviewOpen(false);
  }, []);

  // Resume an existing conversation: load its stored history and make it active.
  const handleSelectConversation = useCallback(
    async (id: string) => {
      if (streamingStatus.isLoading) return;
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
    [fetchConversationMessages, streamingStatus.isLoading],
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
      (newConversationId?: string) => {
        // 更新 conversation_id
        if (newConversationId) {
          setConversationId(newConversationId);
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
          flexDirection: 'column',
          overflow: 'hidden',
          height: 'clamp(420px, 72dvh, 760px)',
          borderRadius: 2,
          bgcolor: 'background.default',
        }}
      >
        <ChatHeader
          statusText={statusText}
          statusColor={statusColor}
          onOpenHistory={() => setHistoryOpen(true)}
          onNewChat={handleNewChat}
        />
        <ProgressIndicator streamingStatus={streamingStatus} />
        <MessageArea messages={messages} onPreview={handlePreview} active={active} />
        <ChatInput
          value={inputValue}
          onChange={setInputValue}
          onSend={handleSendMessage}
          onKeyPress={handleKeyPress}
          disabled={streamingStatus.isLoading}
        />
        <ConversationDrawer
          open={historyOpen}
          onClose={() => setHistoryOpen(false)}
          activeConversationId={conversationId}
          onSelect={handleSelectConversation}
          onNewChat={handleNewChat}
          containerEl={panelEl}
        />
      </Paper>

      <PreviewDialog open={previewOpen} onClose={handleClosePreview} block={previewBlock} />
    </>
  );
}

export default AiAssistantPanel;
