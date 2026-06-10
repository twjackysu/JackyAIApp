import { OpenInFull, Visibility } from '@mui/icons-material';
import PersonIcon from '@mui/icons-material/Person';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import { Avatar, Box, Button, Paper, Stack, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { Message } from '../types';

import MarkdownMessage from '@/components/MarkdownMessage';
import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

interface MessageAreaWithBlocksProps {
  messages: Message[];
  onPreview: (block: ParsedBlock) => void;
  style?: React.CSSProperties;
  ref?: React.Ref<HTMLDivElement>;
}

// 單個區塊渲染組件
const BlockRenderer = ({
  block,
  onPreview,
}: {
  block: ParsedBlock;
  onPreview: (block: ParsedBlock) => void;
}) => {
  const theme = useTheme();

  if (block.contentType === 'markdown') {
    return <MarkdownMessage content={block.content} isOwnMessage={false} theme={theme} />;
  }

  // HTML 區塊：顯示為「預覽」按鈕，點擊後另開寬闊的預覽視窗
  const streaming = !block.isComplete;
  return (
    <Box sx={{ my: 1 }}>
      <Button
        variant="contained"
        size="small"
        disableElevation
        startIcon={streaming ? <Visibility /> : <OpenInFull />}
        onClick={() => onPreview(block)}
        sx={{
          textTransform: 'none',
          borderRadius: 2,
          bgcolor: theme.palette.mode === 'dark' ? 'primary.dark' : 'primary.main',
          '&:hover': { bgcolor: 'primary.dark' },
        }}
      >
        {streaming ? 'HTML 產生中…點擊預覽' : '開啟 HTML 預覽'}
      </Button>
    </Box>
  );
};

function MessageAreaWithBlocks({ messages, onPreview, style, ref }: MessageAreaWithBlocksProps) {
  const theme = useTheme();

  const scrollbarSx = {
    '&::-webkit-scrollbar': { width: '8px' },
    '&::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
    '&::-webkit-scrollbar-thumb': {
      backgroundColor:
        theme.palette.mode === 'dark' ? 'rgba(255, 255, 255, 0.15)' : 'rgba(0, 0, 0, 0.15)',
      borderRadius: '4px',
      '&:hover': {
        backgroundColor:
          theme.palette.mode === 'dark' ? 'rgba(255, 255, 255, 0.25)' : 'rgba(0, 0, 0, 0.25)',
      },
    },
  };

  return (
    <Box
      sx={{
        flexGrow: 1,
        px: { xs: 2, sm: 4 },
        py: 3,
        overflowY: 'auto',
        bgcolor: 'background.default',
        ...scrollbarSx,
        ...style,
      }}
    >
      <Stack spacing={2.5}>
        {messages.length === 0 && (
          <Box sx={{ display: 'flex', justifyContent: 'flex-start' }}>
            <Avatar sx={{ mr: 1.5, bgcolor: theme.palette.success.main, width: 38, height: 38 }}>
              <SmartToyIcon />
            </Avatar>
            <Paper
              variant="outlined"
              sx={{
                p: 2,
                maxWidth: '85%',
                borderRadius: '4px 16px 16px 16px',
                backgroundColor: theme.palette.background.paper,
                boxShadow: '0 2px 8px rgba(0, 0, 0, 0.06)',
              }}
            >
              <Stack direction="column" spacing={0.5}>
                <Typography
                  variant="caption"
                  sx={{ fontWeight: 700, color: theme.palette.success.main }}
                >
                  AI 助手
                </Typography>
                <MarkdownMessage
                  content="您好！我是您的 AI 助手，有什麼可以幫助您的嗎？"
                  isOwnMessage={false}
                  theme={theme}
                />
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
                alignItems: 'flex-start',
              }}
            >
              {!isOwnMessage && (
                <Avatar
                  sx={{ mr: 1.5, bgcolor: theme.palette.success.main, width: 38, height: 38 }}
                >
                  <SmartToyIcon />
                </Avatar>
              )}
              <Paper
                variant="outlined"
                sx={{
                  px: 2,
                  py: 1.5,
                  maxWidth: '85%',
                  borderRadius: isOwnMessage ? '16px 4px 16px 16px' : '4px 16px 16px 16px',
                  border: isOwnMessage ? 'none' : `1px solid ${theme.palette.divider}`,
                  backgroundColor: isOwnMessage
                    ? theme.palette.primary.main
                    : theme.palette.background.paper,
                  color: isOwnMessage
                    ? theme.palette.primary.contrastText
                    : theme.palette.text.primary,
                  boxShadow: isOwnMessage
                    ? '0 2px 8px rgba(25, 118, 210, 0.25)'
                    : '0 2px 8px rgba(0, 0, 0, 0.06)',
                }}
              >
                <Stack direction="column" spacing={0.5}>
                  {!isOwnMessage && (
                    <Typography
                      variant="caption"
                      sx={{ fontWeight: 700, color: theme.palette.success.main }}
                    >
                      AI 助手
                    </Typography>
                  )}

                  {/* 渲染消息內容 */}
                  {isOwnMessage || !message.parsedBlocks ? (
                    <MarkdownMessage
                      content={message.content}
                      isOwnMessage={isOwnMessage}
                      theme={theme}
                    />
                  ) : (
                    <Box>
                      {message.parsedBlocks.map((block) => (
                        <BlockRenderer key={block.id} block={block} onPreview={onPreview} />
                      ))}
                    </Box>
                  )}

                  <Typography
                    variant="caption"
                    sx={{
                      alignSelf: 'flex-end',
                      opacity: 0.7,
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
                  sx={{ ml: 1.5, bgcolor: theme.palette.primary.main, width: 38, height: 38 }}
                >
                  <PersonIcon />
                </Avatar>
              )}
            </Box>
          );
        })}

        {/* 自動滾動參考點 */}
        <div ref={ref} />
      </Stack>
    </Box>
  );
}

export default MessageAreaWithBlocks;
