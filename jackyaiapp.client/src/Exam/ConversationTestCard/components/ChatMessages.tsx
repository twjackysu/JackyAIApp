import { Avatar, Box, IconButton, Paper, Stack, Tooltip, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import PauseIcon from '@mui/icons-material/Pause';
import { forwardRef, useEffect, useRef } from 'react';
import { ConversationTurn } from '@/apis/examApis/types';
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
      <Paper sx={{ p: 2, mb: 2, maxHeight: 400, overflowY: 'auto' }}>
        <Stack spacing={2}>
          {messages.map((turn, index) => {
            const messageId = `${turn.speaker}-${index}`;
            const isPlaying = playingMessageId === messageId;

            return (
              <Box
                key={index}
                sx={{
                  display: 'flex',
                  justifyContent: turn.speaker === 'user' ? 'flex-end' : 'flex-start',
                }}
              >
                <Box
                  sx={{
                    display: 'flex',
                    flexDirection: turn.speaker === 'user' ? 'row-reverse' : 'row',
                    alignItems: 'flex-start',
                    gap: 1,
                    maxWidth: '70%',
                  }}
                >
                  <Avatar
                    sx={{
                      bgcolor: turn.speaker === 'user' 
                        ? theme.palette.primary.main 
                        : theme.palette.success.main,
                      width: 32,
                      height: 32,
                    }}
                  >
                    {turn.speaker === 'user' ? 'U' : 'AI'}
                  </Avatar>
                  <Box>
                    <Paper
                      sx={{
                        p: 1.5,
                        bgcolor: turn.speaker === 'user' 
                          ? theme.palette.mode === 'dark' 
                            ? 'rgba(25, 118, 210, 0.12)' 
                            : 'rgba(25, 118, 210, 0.08)'
                          : theme.palette.mode === 'dark' 
                            ? 'rgba(46, 125, 50, 0.12)' 
                            : 'rgba(46, 125, 50, 0.08)',
                      }}
                    >
                      <Typography variant="body1">{turn.message}</Typography>
                    </Paper>
                    
                    {/* Audio control for AI messages */}
                    {turn.speaker === 'ai' && (
                      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mt: 0.5 }}>
                        <Tooltip title={isPlaying ? "停止播放" : "播放語音"}>
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
                            {isPlaying ? <PauseIcon fontSize="small" /> : <VolumeUpIcon fontSize="small" />}
                          </IconButton>
                        </Tooltip>
                      </Box>
                    )}
                  </Box>
                </Box>
              </Box>
            );
          })}
          {isResponding && (
            <Box sx={{ display: 'flex', justifyContent: 'flex-start' }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Avatar sx={{ bgcolor: theme.palette.success.main, width: 32, height: 32 }}>
                  AI
                </Avatar>
                <Typography variant="body2" color="text.secondary">
                  正在輸入...
                </Typography>
              </Box>
            </Box>
          )}
          {/* Scroll anchor */}
          <div ref={ref} />
        </Stack>
      </Paper>
    );
  }
);

ChatMessages.displayName = 'ChatMessages';

export default ChatMessages;