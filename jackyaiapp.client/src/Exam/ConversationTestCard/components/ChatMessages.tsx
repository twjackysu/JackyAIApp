import PauseIcon from '@mui/icons-material/Pause';
import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import { Avatar, Box, IconButton, Paper, Stack, Tooltip, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { forwardRef, useEffect, useRef } from 'react';

import { ConversationTurn } from '@/apis/examApis/types';
import MarkdownMessage from '@/components/MarkdownMessage';

import { useAudioPlayback } from '../hooks/useAudioPlayback';

interface ChatMessagesProps {
  messages: ConversationTurn[];
  isResponding: boolean;
}

const ChatMessages = forwardRef<HTMLDivElement, ChatMessagesProps>(
  ({ messages, isResponding }, ref) => {
    const theme = useTheme();
    const { playingMessageId, playMessage, stopPlaying } = useAudioPlayback();
    const lastAIMessageIndexRef = useRef<number>(-1);

    // Auto-play the latest AI message (only when a new one appears)
    useEffect(() => {
      if (messages.length === 0) {
        // Reset when conversation starts fresh
        lastAIMessageIndexRef.current = -1;
        return;
      }

      const latestMessage = messages[messages.length - 1];
      const currentLatestIndex = messages.length - 1;

      if (latestMessage.speaker === 'ai' && currentLatestIndex > lastAIMessageIndexRef.current) {
        // Only play if this is a new AI message
        const messageId = `ai-${currentLatestIndex}`;
        playMessage(latestMessage.message, messageId);
        lastAIMessageIndexRef.current = currentLatestIndex;
      }
    }, [messages, playMessage]);

    return (
      <Box
        sx={{
          flexGrow: 1,
          p: 3,
          overflowY: 'auto',
          bgcolor: 'background.default',
          maxHeight: 400,
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
          {messages.map((turn, index) => {
            const messageId = `${turn.speaker}-${index}`;
            const isPlaying = playingMessageId === messageId;
            const isOwnMessage = turn.speaker === 'user';
            const timestamp = new Date().toLocaleTimeString([], {
              hour: '2-digit',
              minute: '2-digit',
            });

            return (
              <Box
                key={index}
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
                    AI
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
                        AI Assistant
                      </Typography>
                    )}
                    <MarkdownMessage
                      content={turn.message}
                      isOwnMessage={isOwnMessage}
                      theme={theme}
                    />
                    <Box
                      sx={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                      }}
                    >
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
                      {/* Audio control for AI messages */}
                      {turn.speaker === 'ai' && (
                        <Tooltip title={isPlaying ? '停止播放' : '播放語音'}>
                          <IconButton
                            size="small"
                            onClick={() => {
                              if (isPlaying) {
                                stopPlaying();
                              } else {
                                playMessage(turn.message, messageId);
                              }
                            }}
                            sx={{
                              color: isPlaying
                                ? theme.palette.primary.main
                                : theme.palette.text.secondary,
                              '&:hover': {
                                color: theme.palette.primary.main,
                              },
                            }}
                          >
                            {isPlaying ? (
                              <PauseIcon fontSize="small" />
                            ) : (
                              <VolumeUpIcon fontSize="small" />
                            )}
                          </IconButton>
                        </Tooltip>
                      )}
                    </Box>
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
                    U
                  </Avatar>
                )}
              </Box>
            );
          })}
          {isResponding && (
            <Box sx={{ display: 'flex', justifyContent: 'flex-start', mb: 2 }}>
              <Avatar sx={{ mr: 1, bgcolor: theme.palette.success.main, width: 40, height: 40 }}>
                AI
              </Avatar>
              <Paper
                variant="outlined"
                sx={{
                  p: 1.5,
                  borderRadius: '16px 16px 16px 0',
                  backgroundColor: theme.palette.background.paper,
                  boxShadow: '0px 1px 2px rgba(0, 0, 0, 0.05)',
                }}
              >
                <Typography variant="body2" color="text.secondary">
                  正在輸入...
                </Typography>
              </Paper>
            </Box>
          )}
          {/* Scroll anchor */}
          <div ref={ref} />
        </Stack>
      </Box>
    );
  },
);

ChatMessages.displayName = 'ChatMessages';

export default ChatMessages;
