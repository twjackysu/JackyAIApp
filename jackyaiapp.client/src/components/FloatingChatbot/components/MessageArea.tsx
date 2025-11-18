import PersonIcon from '@mui/icons-material/Person';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import { Avatar, Box, Paper, Stack, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { forwardRef } from 'react';

import MarkdownMessage from '@/components/MarkdownMessage';

import { Message } from '../types';

interface MessageAreaProps {
  messages: Message[];
}

const MessageArea = forwardRef<HTMLDivElement, MessageAreaProps>(({ messages }, messagesEndRef) => {
  const theme = useTheme();

  return (
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
              theme.palette.mode === 'dark' ? 'rgba(255, 255, 255, 0.25)' : 'rgba(0, 0, 0, 0.25)',
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
  );
});

MessageArea.displayName = 'MessageArea';

export default MessageArea;
