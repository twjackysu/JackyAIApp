import ChatIcon from '@mui/icons-material/Chat';
import CloseIcon from '@mui/icons-material/Close';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import PersonIcon from '@mui/icons-material/Person';
import SendIcon from '@mui/icons-material/Send';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import {
  AppBar,
  Avatar,
  Box,
  Dialog,
  Fab,
  IconButton,
  LinearProgress,
  Paper,
  Stack,
  TextField,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import { useEffect, useRef, useState } from 'react';
import { useChatStreaming } from '../hooks/useChatStreaming';
import MarkdownMessage from './MarkdownMessage';

interface Message {
  id: string;
  content: string;
  isUser: boolean;
  timestamp: Date;
}

interface Position {
  x: number;
  y: number;
}

function FloatingChatbot() {
  const theme = useTheme();
  const isSmallScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [isOpen, setIsOpen] = useState(false);
  const [position, setPosition] = useState<Position>(() => ({
    x: window.innerWidth - 80, // 距離右邊 80px
    y: window.innerHeight - 80, // 距離底部 80px
  }));
  const [isDragging, setIsDragging] = useState(false);
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputValue, setInputValue] = useState('');
  const [conversationId, setConversationId] = useState<string>('');
  const fabRef = useRef<HTMLButtonElement>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const { sendStreamingMessage, streamingStatus } = useChatStreaming();

  const handleMouseDown = (e: React.MouseEvent) => {
    if (!fabRef.current) return;

    const rect = fabRef.current.getBoundingClientRect();
    setDragOffset({
      x: e.clientX - rect.left,
      y: e.clientY - rect.top,
    });
    setIsDragging(true);
  };

  const handleMouseMove = (e: MouseEvent) => {
    if (!isDragging) return;

    const newX = e.clientX - dragOffset.x;
    const newY = e.clientY - dragOffset.y;

    // 確保按鈕不會拖出視窗
    const maxX = window.innerWidth - 56; // FAB 的寬度
    const maxY = window.innerHeight - 56; // FAB 的高度

    setPosition({
      x: Math.max(0, Math.min(newX, maxX)),
      y: Math.max(0, Math.min(newY, maxY)),
    });
  };

  const handleMouseUp = () => {
    setIsDragging(false);
  };

  useEffect(() => {
    if (isDragging) {
      document.addEventListener('mousemove', handleMouseMove);
      document.addEventListener('mouseup', handleMouseUp);

      return () => {
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
      };
    }
  }, [isDragging, dragOffset]);

  // 處理視窗大小改變
  useEffect(() => {
    const handleResize = () => {
      setPosition((prev) => ({
        x: Math.min(prev.x, window.innerWidth - 56),
        y: Math.min(prev.y, window.innerHeight - 56),
      }));
    };

    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

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

  const getAgentStatusText = () => {
    if (streamingStatus.isLoading) {
      return streamingStatus.statusText || '處理中...';
    }
    return '線上';
  };

  const getStatusColor = () => {
    if (streamingStatus.isLoading) {
      return 'info.main'; // 藍色表示工作中
    }
    return 'success.main'; // 綠色表示線上
  };

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
    };

    setMessages((prev) => [...prev, botMessage]);

    console.log('Sending message with conversationId:', conversationId);

    await sendStreamingMessage(
      userMessage.content,
      conversationId,
      // onMessageUpdate
      (content: string) => {
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
      {/* 懸浮按鈕 */}
      <Fab
        ref={fabRef}
        color="primary"
        aria-label="chatbot"
        onMouseDown={handleMouseDown}
        onClick={handleFabClick}
        sx={{
          position: 'fixed',
          left: position.x,
          top: position.y,
          zIndex: 1300,
          cursor: isDragging ? 'grabbing' : 'grab',
          '&:hover': {
            transform: 'scale(1.1)',
          },
          transition: 'transform 0.2s ease-in-out',
        }}
      >
        <ChatIcon />
      </Fab>

      {/* 聊天對話框 */}
      <Dialog
        open={isOpen}
        onClose={handleClose}
        maxWidth="sm"
        fullWidth
        PaperProps={{
          sx: {
            height: isSmallScreen ? '100vh' : '600px',
            display: 'flex',
            flexDirection: 'column',
            bgcolor: 'background.default',
          },
        }}
      >
        {/* Chat Header with AppBar style */}
        <AppBar
          position="static"
          color="inherit"
          elevation={0}
          sx={{ borderBottom: '1px solid', borderColor: 'divider' }}
        >
          <Toolbar>
            <Avatar
              sx={{
                mr: 2,
                bgcolor: theme.palette.success.main,
                width: 40,
                height: 40,
              }}
            >
              <SmartToyIcon />
            </Avatar>
            <Stack>
              <Typography variant="h6" component="div">
                AI 助手
              </Typography>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                <Box
                  sx={{
                    width: 8,
                    height: 8,
                    borderRadius: '50%',
                    bgcolor: getStatusColor(),
                  }}
                />
                <Typography variant="caption" color="text.secondary">
                  {getAgentStatusText()}
                </Typography>
              </Box>
            </Stack>
            <Box sx={{ flexGrow: 1 }} />
            <IconButton color="inherit">
              <MoreVertIcon />
            </IconButton>
            <IconButton onClick={handleClose} color="inherit">
              <CloseIcon />
            </IconButton>
          </Toolbar>
        </AppBar>

        {/* 進度條和狀態顯示 */}
        {streamingStatus.isLoading && (
          <Box sx={{ px: 2, py: 1, borderBottom: '1px solid', borderColor: 'divider' }}>
            <LinearProgress sx={{ mb: 1, height: 4, borderRadius: 3 }} />
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{ display: 'flex', alignItems: 'center' }}
            >
              <SmartToyIcon sx={{ fontSize: 14, mr: 0.5 }} />
              {streamingStatus.statusText}
              {streamingStatus.workflowSteps > 0 && (
                <Typography variant="caption" sx={{ ml: 1 }}>
                  ({streamingStatus.completedSteps}/{streamingStatus.workflowSteps})
                </Typography>
              )}
            </Typography>
          </Box>
        )}

        {/* Message Display Area - ChatRoom style */}
        <Box
          sx={{
            flexGrow: 1,
            p: 3,
            overflowY: 'auto',
            bgcolor: 'background.default',
            // 更精美的聊天區域 scrollbar
            '&::-webkit-scrollbar': {
              width: '6px',
            },
            '&::-webkit-scrollbar-track': {
              backgroundColor: 'transparent',
            },
            '&::-webkit-scrollbar-thumb': {
              backgroundColor:
                theme.palette.mode === 'dark' ? 'rgba(255, 255, 255, 0.15)' : 'rgba(0, 0, 0, 0.15)',
              borderRadius: '3px',
              '&:hover': {
                backgroundColor:
                  theme.palette.mode === 'dark'
                    ? 'rgba(255, 255, 255, 0.25)'
                    : 'rgba(0, 0, 0, 0.25)',
              },
            },
          }}
        >
          <Stack spacing={1}>
            {messages.length === 0 && (
              <Box sx={{ display: 'flex', justifyContent: 'flex-start', mb: 2 }}>
                <Avatar sx={{ mr: 1, bgcolor: theme.palette.success.main, width: 40, height: 40 }}>
                  <SmartToyIcon />
                </Avatar>
                <Paper
                  variant="outlined"
                  sx={{
                    p: 1.5,
                    maxWidth: '70%',
                    borderRadius: '16px 16px 16px 0',
                    backgroundColor: theme.palette.background.paper,
                    boxShadow: '0px 1px 2px rgba(0, 0, 0, 0.05)',
                  }}
                >
                  <Stack direction="column" spacing={0.5}>
                    <Typography
                      variant="caption"
                      fontWeight="bold"
                      sx={{ color: theme.palette.success.main }}
                    >
                      AI 助手
                    </Typography>
                    <MarkdownMessage
                      content="您好！我是您的 AI 助手，有什麼可以幫助您的嗎？"
                      isOwnMessage={false}
                      theme={theme}
                    />
                    <Typography
                      variant="caption"
                      sx={{
                        alignSelf: 'flex-end',
                        color: theme.palette.text.secondary,
                      }}
                    >
                      {new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </Typography>
                  </Stack>
                </Paper>
              </Box>
            )}

            {messages.map((message) => {
              const isOwnMessage = message.isUser;
              const timestamp = message.timestamp.toLocaleTimeString([], {
                hour: '2-digit',
                minute: '2-digit',
              });

              return (
                <Box
                  key={message.id}
                  sx={{
                    display: 'flex',
                    justifyContent: isOwnMessage ? 'flex-end' : 'flex-start',
                    mb: 2,
                  }}
                >
                  {!isOwnMessage && (
                    <Avatar
                      sx={{
                        mr: 1,
                        bgcolor: theme.palette.success.main,
                        width: 40,
                        height: 40,
                      }}
                    >
                      <SmartToyIcon />
                    </Avatar>
                  )}
                  <Paper
                    variant="outlined"
                    sx={{
                      p: 1.5,
                      maxWidth: '70%',
                      borderRadius: isOwnMessage ? '16px 16px 0 16px' : '16px 16px 16px 0',
                      backgroundColor: isOwnMessage
                        ? theme.palette.primary.main
                        : theme.palette.background.paper,
                      color: isOwnMessage
                        ? theme.palette.primary.contrastText
                        : theme.palette.text.primary,
                      boxShadow: '0px 1px 2px rgba(0, 0, 0, 0.05)',
                    }}
                  >
                    <Stack direction="column" spacing={0.5}>
                      {!isOwnMessage && (
                        <Typography
                          variant="caption"
                          fontWeight="bold"
                          sx={{ color: theme.palette.success.main }}
                        >
                          AI 助手
                        </Typography>
                      )}
                      <MarkdownMessage
                        content={message.content}
                        isOwnMessage={isOwnMessage}
                        theme={theme}
                      />
                      <Typography
                        variant="caption"
                        sx={{
                          alignSelf: 'flex-end',
                          color: isOwnMessage
                            ? theme.palette.primary.contrastText
                            : theme.palette.text.secondary,
                        }}
                      >
                        {timestamp}
                      </Typography>
                    </Stack>
                  </Paper>
                  {isOwnMessage && (
                    <Avatar
                      sx={{
                        ml: 1,
                        bgcolor: theme.palette.primary.main,
                        width: 40,
                        height: 40,
                      }}
                    >
                      <PersonIcon />
                    </Avatar>
                  )}
                </Box>
              );
            })}

            {/* 自動滾動參考點 */}
            <div ref={messagesEndRef} />
          </Stack>
        </Box>

        {/* Message Input - ChatRoom style */}
        <Paper
          elevation={3}
          sx={{
            p: 2,
            display: 'flex',
            alignItems: 'center',
            borderRadius: 0,
            backgroundColor: theme.palette.background.paper,
          }}
        >
          <TextField
            fullWidth
            multiline
            maxRows={3}
            variant="outlined"
            placeholder="輸入訊息..."
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            onKeyPress={handleKeyPress}
            disabled={streamingStatus.isLoading}
            sx={{
              mr: 2,
              '& .MuiOutlinedInput-root': {
                borderRadius: 8,
              },
            }}
          />
          <IconButton
            color="primary"
            onClick={handleSendMessage}
            disabled={!inputValue.trim() || streamingStatus.isLoading}
          >
            <SendIcon />
          </IconButton>
        </Paper>
      </Dialog>
    </>
  );
}

export default FloatingChatbot;
