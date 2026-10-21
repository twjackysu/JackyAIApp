import OpenInFullIcon from '@mui/icons-material/OpenInFull';
import PersonIcon from '@mui/icons-material/Person';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import VisibilityIcon from '@mui/icons-material/Visibility';
import { Avatar, Box, Button, Chip, Paper, Stack, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useEffect, useRef } from 'react';

import { GREETING_SUGGESTIONS } from '../examples';
import { Message } from '../types';

import AttachmentChip from './AttachmentChip';

import { TRUNCATED_NOTE } from '@/apis/chatbotApis/chatErrors';
import { attachmentContentUrl } from '@/apis/chatbotApis/types';
import MarkdownMessage from '@/components/MarkdownMessage';
import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

/** Within this many pixels of the end, the reader counts as "following" the conversation. */
const FOLLOW_THRESHOLD_PX = 80;

interface MessageAreaProps {
  messages: Message[];
  onPreview: (block: ParsedBlock) => void;
  /** False while the panel's tab is hidden: a hidden element has no scroll position to keep. */
  active: boolean;
  /** A suggested question under the greeting was picked: put it in the message box. */
  onSuggest?: (question: string) => void;
}

const GREETING =
  '您好！我是台股 AI 助手，可以查個股走勢、財報與股利、三大法人籌碼、ETF、期貨選擇權和景氣指標，也能分析您附上的檔案或截圖。想問什麼呢？';

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
        startIcon={streaming ? <VisibilityIcon /> : <OpenInFullIcon />}
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

function MessageArea({ messages, onPreview, active, onSuggest }: MessageAreaProps) {
  const theme = useTheme();
  const scrollRef = useRef<HTMLDivElement>(null);
  const followingRef = useRef(true);
  const messageCountRef = useRef(messages.length);

  // Scrolls this box itself, never the page: the chat now sits inside the page, and
  // scrollIntoView on a sentinel would drag the whole page along with every streamed token.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el || !active) return;
    // A new bubble (the reader's own message, or the reply starting) always follows to the end;
    // a bubble that is merely growing as it streams follows only if the reader has not scrolled up.
    if (messages.length !== messageCountRef.current) {
      messageCountRef.current = messages.length;
      followingRef.current = true;
    }
    if (followingRef.current) el.scrollTop = el.scrollHeight;
  }, [messages, active]);

  const handleScroll = () => {
    const el = scrollRef.current;
    if (!el || !active) return;
    followingRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < FOLLOW_THRESHOLD_PX;
  };

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
      ref={scrollRef}
      onScroll={handleScroll}
      sx={{
        flex: 1,
        minHeight: 0,
        px: { xs: 1.5, sm: 3 },
        py: 2.5,
        overflowY: 'auto',
        bgcolor: 'background.default',
        ...scrollbarSx,
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
                <MarkdownMessage content={GREETING} isOwnMessage={false} theme={theme} />
                {onSuggest && (
                  <Box
                    data-testid="greeting-suggestions"
                    sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, pt: 0.5 }}
                  >
                    {GREETING_SUGGESTIONS.map((q) => (
                      <Chip
                        key={q}
                        label={q}
                        size="small"
                        variant="outlined"
                        clickable
                        onClick={() => onSuggest(q)}
                      />
                    ))}
                  </Box>
                )}
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

                  {message.attachments && message.attachments.length > 0 && (
                    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mb: 0.5 }}>
                      {message.attachments.map((a) => {
                        const isFile = a.kind === 'image' || a.kind === 'pdf';
                        return (
                          <AttachmentChip
                            key={a.id}
                            fileName={a.fileName}
                            kind={a.kind}
                            truncated={a.truncated}
                            pageCount={a.pageCount}
                            thumbnailUrl={
                              a.kind === 'image' ? attachmentContentUrl(a.id) : undefined
                            }
                            href={isFile ? attachmentContentUrl(a.id) : undefined}
                            onBubble={isOwnMessage}
                          />
                        );
                      })}
                    </Box>
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

                  {message.truncated && (
                    <Typography variant="caption" sx={{ color: 'warning.main' }}>
                      {TRUNCATED_NOTE}
                    </Typography>
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
      </Stack>
    </Box>
  );
}

export default MessageArea;
