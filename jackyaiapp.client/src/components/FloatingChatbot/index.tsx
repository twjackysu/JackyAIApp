import { useState, useRef, useEffect } from 'react';
import { useChatStreaming } from '@/hooks/useChatStreaming';
import { useDraggable } from './hooks/useDraggable';
import { useAgentStatus } from './hooks/useAgentStatus';
import { Message } from './types';
import FloatingButton from './components/FloatingButton';
import ChatDialog from './components/ChatDialog';

function FloatingChatbot() {
  const [isOpen, setIsOpen] = useState(false);
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputValue, setInputValue] = useState('');
  const [conversationId, setConversationId] = useState<string>('');
  const messagesEndRef = useRef<HTMLDivElement>(null);
  
  const { sendStreamingMessage, streamingStatus } = useChatStreaming();
  const { position, isDragging, fabRef, handleMouseDown } = useDraggable();
  const { statusText, statusColor } = useAgentStatus(streamingStatus);

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

    setMessages(prev => [...prev, userMessage]);
    setInputValue('');

    // 創建初始的 bot 訊息
    const botMessage: Message = {
      id: (Date.now() + 1).toString(),
      content: '',
      isUser: false,
      timestamp: new Date(),
    };
    
    setMessages(prev => [...prev, botMessage]);

    console.log('Sending message with conversationId:', conversationId);
    
    await sendStreamingMessage(
      userMessage.content,
      conversationId,
      // onMessageUpdate
      (content: string) => {
        setMessages(prev => 
          prev.map(msg => 
            msg.id === botMessage.id 
              ? { ...msg, content } 
              : msg
          )
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
        setMessages(prev => 
          prev.map(msg => 
            msg.id === botMessage.id 
              ? { ...msg, content: `錯誤: ${error}` } 
              : msg
          )
        );
      }
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

      <ChatDialog
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
      />
    </>
  );
}

export default FloatingChatbot;