import { useCallback, useEffect, useRef, useState } from 'react';

import ChatDialogWithPreview from './components/ChatDialogWithPreview';
import FloatingButton from './components/FloatingButton';
import { useAgentStatus } from './hooks/useAgentStatus';
import { useDraggable } from './hooks/useDraggable';
import { Message } from './types';

import { useGetUserInfoQuery } from '@/apis/accountApis';
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

function FloatingChatbot() {
  const [isOpen, setIsOpen] = useState(false);
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputValue, setInputValue] = useState('');
  const [conversationId, setConversationId] = useState<string>('');
  const [currentStreamingContent, setCurrentStreamingContent] = useState<string>('');

  // On-demand preview (HTML / Markdown) — opened from a button inside a message.
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewBlock, setPreviewBlock] = useState<ParsedBlock | null>(null);

  // Conversation history drawer.
  const [historyOpen, setHistoryOpen] = useState(false);

  const messagesEndRef = useRef<HTMLDivElement>(null);

  const [fetchConversationMessages] = useLazyGetConversationMessagesQuery();
  const { sendStreamingMessage, streamingStatus } = useChatStreaming();
  const { position, isDragging, fabRef, handleMouseDown } = useDraggable();
  const { statusText, statusColor } = useAgentStatus(streamingStatus);
  const { blocks, currentHtmlBlock } = useStreamingHtmlParser(currentStreamingContent);

  const handlePreview = (block: ParsedBlock) => {
    setPreviewBlock(block);
    setPreviewOpen(true);
  };

  const handleClosePreview = () => setPreviewOpen(false);

  // Start a fresh conversation (Dify assigns a new id on the next message).
  const handleNewChat = useCallback(() => {
    setMessages([]);
    setConversationId('');
    setCurrentStreamingContent('');
    setPreviewOpen(false);
  }, []);

  // Resume an existing conversation: load its history from Dify and make it active.
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

  // 自動滾動到底部
  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  // 當訊息更新時自動滾動
  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  // 當 streaming 狀態變化時也滾動（確保即時更新時也能跟上）
  useEffect(() => {
    if (streamingStatus.isLoading) {
      scrollToBottom();
    }
  }, [streamingStatus.currentEvent, streamingStatus.isLoading]);

  const handleFabClick = (e: React.MouseEvent) => {
    // 如果正在拖拉就不開啟聊天
    if (isDragging) {
      e.preventDefault();
      return;
    }
    setIsOpen(true);
  };

  const handleClose = () => {
    setIsOpen(false);
    setHistoryOpen(false);
  };

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
        setMessages((prev) =>
          prev.map((msg) =>
            msg.id === botMessage.id ? { ...msg, content: `錯誤: ${error}` } : msg,
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
      <FloatingButton
        position={position}
        isDragging={isDragging}
        fabRef={fabRef}
        onMouseDown={handleMouseDown}
        onClick={handleFabClick}
      />

      <ChatDialogWithPreview
        open={isOpen}
        onClose={handleClose}
        messages={messages}
        inputValue={inputValue}
        onInputChange={setInputValue}
        onSendMessage={handleSendMessage}
        onKeyPress={handleKeyPress}
        streamingStatus={streamingStatus}
        statusText={statusText}
        statusColor={statusColor}
        messagesEndRef={messagesEndRef}
        onOpenHistory={() => setHistoryOpen(true)}
        onNewChat={handleNewChat}
        historyOpen={historyOpen}
        onCloseHistory={() => setHistoryOpen(false)}
        activeConversationId={conversationId}
        onSelectConversation={handleSelectConversation}
        onPreview={handlePreview}
        previewBlock={previewBlock}
        previewOpen={previewOpen}
        onClosePreview={handleClosePreview}
      />
    </>
  );
}

/** Wrapper that only renders chatbot when user is authenticated (no redirect) */
function AuthAwareFloatingChatbot() {
  const { data: userInfo, isLoading } = useGetUserInfoQuery();
  if (isLoading || !userInfo?.data) return null;
  return <FloatingChatbot />;
}

export default AuthAwareFloatingChatbot;
