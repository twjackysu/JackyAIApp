import { useChatStreaming } from '@/hooks/useChatStreaming';
import { useStreamingHtmlParser } from '@/hooks/useStreamingHtmlParser';
import { useEffect, useRef, useState } from 'react';
import ChatDialogWithPreview from './components/ChatDialogWithPreview';
import FloatingButton from './components/FloatingButton';
import { useAgentStatus } from './hooks/useAgentStatus';
import { useDraggable } from './hooks/useDraggable';
import { Message } from './types';

function FloatingChatbot() {
  const [isOpen, setIsOpen] = useState(false);
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputValue, setInputValue] = useState('');
  const [conversationId, setConversationId] = useState<string>('');
  const [currentStreamingContent, setCurrentStreamingContent] = useState<string>('');
  const [showHtmlViewer, setShowHtmlViewer] = useState(false);
  const [selectedHtmlContent, setSelectedHtmlContent] = useState<string>('');
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const { sendStreamingMessage, streamingStatus } = useChatStreaming();
  const { position, isDragging, fabRef, handleMouseDown } = useDraggable();
  const { statusText, statusColor } = useAgentStatus(streamingStatus);
  const { blocks, currentHtmlBlock, isStreamingHtml } =
    useStreamingHtmlParser(currentStreamingContent);

  // 處理 HTML 預覽
  const handleHtmlPreview = (content: string) => {
    setSelectedHtmlContent(content);
    setShowHtmlViewer(true);
  };

  // 當檢測到 HTML streaming 時自動開啟預覽面板並實時更新內容
  useEffect(() => {
    if (isStreamingHtml && currentHtmlBlock) {
      setSelectedHtmlContent(currentHtmlBlock.content);
      setShowHtmlViewer(true);
    }
  }, [isStreamingHtml, currentHtmlBlock]);

  // 實時更新右側預覽內容（如果正在顯示且有 streaming HTML）
  useEffect(() => {
    if (showHtmlViewer && currentHtmlBlock) {
      setSelectedHtmlContent(currentHtmlBlock.content);
    }
  }, [currentHtmlBlock?.content, showHtmlViewer]);

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
  }, [streamingStatus.currentEvent]);

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

    console.log('Sending message with conversationId:', conversationId);

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
          console.log('Received new conversationId:', newConversationId);
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
        currentHtmlBlock={currentHtmlBlock}
        showHtmlViewer={showHtmlViewer}
        selectedHtmlContent={selectedHtmlContent}
        onHtmlPreview={handleHtmlPreview}
      />
    </>
  );
}

export default FloatingChatbot;
