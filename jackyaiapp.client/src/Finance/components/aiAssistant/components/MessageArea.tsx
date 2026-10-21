import CandlestickChartIcon from '@mui/icons-material/CandlestickChart';
import OpenInFullIcon from '@mui/icons-material/OpenInFull';
import PieChartOutlineIcon from '@mui/icons-material/PieChartOutlined';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import VisibilityIcon from '@mui/icons-material/Visibility';
import { Box, Button, ButtonBase, Paper, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useEffect, useRef } from 'react';

import { GREETING_SUGGESTIONS } from '../examples';
import { ACCENT, accentWash, hairline } from '../style';
import { Message } from '../types';

import AssistantMark from './AssistantMark';
import AttachmentChip from './AttachmentChip';

import { TRUNCATED_NOTE } from '@/apis/chatbotApis/chatErrors';
import { attachmentContentUrl } from '@/apis/chatbotApis/types';
import MarkdownMessage from '@/components/MarkdownMessage';
import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

/** Within this many pixels of the end, the reader counts as "following" the conversation. */
const FOLLOW_THRESHOLD_PX = 80;

/** The conversation reads as one column of this width, however wide the panel. */
const COLUMN_MAX_WIDTH = 780;

interface MessageAreaProps {
  messages: Message[];
  onPreview: (block: ParsedBlock) => void;
  /** False while the panel's tab is hidden: a hidden element has no scroll position to keep. */
  active: boolean;
  /** A suggested question under the greeting was picked: put it in the message box. */
  onSuggest?: (question: string) => void;
}

export const GREETING_TITLE = '今天想研究哪一檔？';
export const GREETING =
  '我是台股 AI 助手，可以查個股走勢、財報與股利、三大法人籌碼、ETF、期貨選擇權和景氣指標，也能分析您附上的檔案或截圖。';

/** One icon per suggestion, in GREETING_SUGGESTIONS' order (trend, financials, futures, ETF). */
const SUGGESTION_ICONS = [
  ShowChartIcon,
  ReceiptLongIcon,
  CandlestickChartIcon,
  PieChartOutlineIcon,
];

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
        variant="outlined"
        size="small"
        startIcon={streaming ? <VisibilityIcon /> : <OpenInFullIcon />}
        onClick={() => onPreview(block)}
        sx={{ borderRadius: 2, borderColor: hairline(theme), color: 'text.primary' }}
      >
        {streaming ? 'HTML 產生中…點擊預覽' : '開啟 HTML 預覽'}
      </Button>
    </Box>
  );
};

/** An empty conversation: what the assistant does, and a few questions to start from. */
const Welcome = ({ onSuggest }: { onSuggest?: (question: string) => void }) => {
  const theme = useTheme();
  return (
    <Box
      sx={{
        minHeight: '100%',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        textAlign: 'center',
        gap: 1.5,
        py: 3,
      }}
    >
      <AssistantMark size={48} />
      <Typography variant="h6" component="h2" sx={{ fontWeight: 700, mt: 1 }}>
        {GREETING_TITLE}
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ maxWidth: 520, lineHeight: 1.7 }}>
        {GREETING}
      </Typography>
      {onSuggest && (
        <Box
          data-testid="greeting-suggestions"
          sx={{
            mt: 1.5,
            width: '100%',
            maxWidth: 560,
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' },
            gap: 1,
          }}
        >
          {GREETING_SUGGESTIONS.map((q, i) => {
            const Icon = SUGGESTION_ICONS[i % SUGGESTION_ICONS.length];
            return (
              <ButtonBase
                key={q}
                onClick={() => onSuggest(q)}
                sx={{
                  justifyContent: 'flex-start',
                  gap: 1.25,
                  px: 1.75,
                  py: 1.25,
                  borderRadius: 3,
                  textAlign: 'left',
                  border: `1px solid ${hairline(theme)}`,
                  bgcolor:
                    theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.025)' : 'rgba(0,0,0,0.015)',
                  transition: 'border-color 120ms, background-color 120ms',
                  '&:hover, &.Mui-focusVisible': {
                    borderColor: ACCENT,
                    bgcolor: accentWash(theme, 0.08),
                  },
                }}
              >
                <Icon sx={{ fontSize: 18, color: ACCENT }} />
                <Typography variant="body2">{q}</Typography>
              </ButtonBase>
            );
          })}
        </Box>
      )}
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

  return (
    <Box
      ref={scrollRef}
      onScroll={handleScroll}
      sx={{
        flex: 1,
        minHeight: 0,
        overflowY: 'auto',
        px: { xs: 1.5, sm: 3 },
        py: 3,
        '&::-webkit-scrollbar': { width: 8 },
        '&::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
        '&::-webkit-scrollbar-thumb': { backgroundColor: hairline(theme), borderRadius: 4 },
      }}
    >
      <Box
        sx={{
          maxWidth: COLUMN_MAX_WIDTH,
          mx: 'auto',
          minHeight: '100%',
          display: 'flex',
          flexDirection: 'column',
          gap: 3,
        }}
      >
        {messages.length === 0 && <Welcome onSuggest={onSuggest} />}

        {messages.map((message) => {
          const attachments = message.attachments && message.attachments.length > 0 && (
            <Box
              sx={{
                display: 'flex',
                flexWrap: 'wrap',
                gap: 0.5,
                justifyContent: message.isUser ? 'flex-end' : 'flex-start',
                mb: 0.75,
              }}
            >
              {message.attachments.map((a) => {
                const isFile = a.kind === 'image' || a.kind === 'pdf';
                return (
                  <AttachmentChip
                    key={a.id}
                    fileName={a.fileName}
                    kind={a.kind}
                    truncated={a.truncated}
                    pageCount={a.pageCount}
                    thumbnailUrl={a.kind === 'image' ? attachmentContentUrl(a.id) : undefined}
                    href={isFile ? attachmentContentUrl(a.id) : undefined}
                  />
                );
              })}
            </Box>
          );

          // The reader's own message: a soft bubble on the right.
          if (message.isUser) {
            return (
              <Box
                key={message.id}
                data-testid="user-message"
                sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end' }}
              >
                {attachments}
                <Paper
                  elevation={0}
                  sx={{
                    px: 2,
                    py: 1.25,
                    maxWidth: '85%',
                    borderRadius: '18px 18px 6px 18px',
                    bgcolor: accentWash(theme, 0.13),
                    border: `1px solid ${accentWash(theme, 0.22)}`,
                    color: 'text.primary',
                  }}
                >
                  <MarkdownMessage content={message.content} isOwnMessage theme={theme} />
                </Paper>
              </Box>
            );
          }

          // The assistant's answer: no bubble, the full column, its mark beside it.
          return (
            <Box key={message.id} sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start' }}>
              <Box sx={{ pt: 0.25 }}>
                <AssistantMark size={26} />
              </Box>
              <Box sx={{ flex: 1, minWidth: 0 }}>
                {attachments}
                {!message.parsedBlocks?.length && message.content ? (
                  <MarkdownMessage content={message.content} isOwnMessage={false} theme={theme} />
                ) : !message.parsedBlocks?.length ? (
                  // The answer has not started: three dots in place of text.
                  <Box
                    aria-label="AI 助手思考中"
                    sx={{
                      display: 'flex',
                      gap: 0.6,
                      py: 1,
                      '& span': {
                        width: 6,
                        height: 6,
                        borderRadius: '50%',
                        bgcolor: 'text.secondary',
                        animation: 'assistantDot 1.2s infinite ease-in-out',
                      },
                      '& span:nth-of-type(2)': { animationDelay: '0.15s' },
                      '& span:nth-of-type(3)': { animationDelay: '0.3s' },
                      '@keyframes assistantDot': {
                        '0%, 80%, 100%': { opacity: 0.25, transform: 'translateY(0)' },
                        '40%': { opacity: 1, transform: 'translateY(-3px)' },
                      },
                    }}
                  >
                    <span />
                    <span />
                    <span />
                  </Box>
                ) : (
                  <Box>
                    {message.parsedBlocks!.map((block) => (
                      <BlockRenderer key={block.id} block={block} onPreview={onPreview} />
                    ))}
                  </Box>
                )}
                {message.truncated && (
                  <Typography
                    variant="caption"
                    sx={{ display: 'block', mt: 0.5, color: 'warning.main' }}
                  >
                    {TRUNCATED_NOTE}
                  </Typography>
                )}
              </Box>
            </Box>
          );
        })}
      </Box>
    </Box>
  );
}

export default MessageArea;
