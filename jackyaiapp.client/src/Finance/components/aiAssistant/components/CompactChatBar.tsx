import OpenInFullIcon from '@mui/icons-material/OpenInFull';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import InputBase from '@mui/material/InputBase';
import { alpha, useTheme } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { useEffect, useRef, useState } from 'react';

import { signInAndComeBack } from '../../../utils/signIn';
import { EXAMPLE_QUESTIONS } from '../examples';

import { ActionTooltip } from '@/components/hints';

interface CompactChatBarProps {
  value: string;
  onChange: (value: string) => void;
  /** The visitor wants to use the assistant: open the full panel. */
  onActivate: () => void;
  /** `loading`: we do not know yet who the visitor is. */
  state: 'loading' | 'ready' | 'signedOut';
  /** A reply is being written in the folded panel. */
  busy: boolean;
  /** The folded panel holds a conversation. */
  hasConversation: boolean;
}

/** How long each example question stays in the idle box before the next one. */
export const EXAMPLE_ROTATE_MS = 4000;

/** The box is idle: nothing typed, nothing going on — it shows example questions in turn. */
const isIdle = ({ state, busy, hasConversation, value }: CompactChatBarProps) =>
  state === 'ready' && !busy && !hasConversation && !value;

const placeholderFor = ({ state, busy, hasConversation }: CompactChatBarProps, example: string) => {
  if (state === 'signedOut') return '登入後即可問 AI 助手台股問題';
  if (state === 'loading') return 'AI 助手';
  if (busy) return 'AI 助手回覆中…';
  if (hasConversation) return '繼續剛才的對話…';
  return `問 AI 助手，例如「${example}」`;
};

/**
 * The AI assistant folded down to one line, about the size of a search box. It is marked as a
 * chat rather than a search — the assistant's icon and name, a question for a placeholder, and
 * the assistant's colours — so it is not mistaken for the stock search below. Putting the cursor
 * in it opens the full conversation.
 */
export function CompactChatBar(props: CompactChatBarProps) {
  const { value, onChange, onActivate, state, busy } = props;
  const theme = useTheme();
  const inputRef = useRef<HTMLInputElement>(null);
  const ready = state === 'ready';
  const accent = theme.palette.success.main;

  // A different question every few seconds shows how much can be asked.
  const [exampleIndex, setExampleIndex] = useState(0);
  const idle = isIdle(props);
  useEffect(() => {
    if (!idle) return;
    const timer = setInterval(
      () => setExampleIndex((i) => (i + 1) % EXAMPLE_QUESTIONS.length),
      EXAMPLE_ROTATE_MS,
    );
    return () => clearInterval(timer);
  }, [idle]);

  return (
    <Box
      // The whole strip is the target: a click anywhere on it lands in the input.
      onClick={() => inputRef.current?.focus()}
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1,
        minHeight: 48,
        px: 1.5,
        borderRadius: 3,
        border: '1px solid',
        borderColor: alpha(accent, 0.45),
        backgroundImage:
          theme.palette.mode === 'dark'
            ? 'linear-gradient(135deg, rgba(46,125,50,0.16), rgba(25,118,210,0.10))'
            : 'linear-gradient(135deg, rgba(46,125,50,0.08), rgba(25,118,210,0.05))',
        cursor: ready ? 'text' : 'default',
        transition: 'border-color 120ms',
        '&:hover': ready ? { borderColor: accent } : undefined,
      }}
    >
      <SmartToyIcon sx={{ color: accent, flexShrink: 0 }} />
      <Typography
        variant="subtitle2"
        sx={{ fontWeight: 700, color: accent, flexShrink: 0, display: { xs: 'none', sm: 'block' } }}
      >
        AI 助手
      </Typography>
      <InputBase
        inputRef={inputRef}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onFocus={onActivate}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault();
            onActivate();
          }
        }}
        readOnly={!ready}
        disabled={state === 'loading'}
        placeholder={placeholderFor(props, EXAMPLE_QUESTIONS[exampleIndex])}
        inputProps={{ 'aria-label': 'AI 助手' }}
        sx={{ flex: 1, minWidth: 0, fontSize: '0.95rem' }}
      />
      {busy && <CircularProgress size={18} sx={{ flexShrink: 0 }} aria-label="AI 助手回覆中" />}
      {state === 'signedOut' ? (
        <Button
          size="small"
          variant="outlined"
          color="success"
          onClick={(e) => {
            e.stopPropagation();
            signInAndComeBack();
          }}
          sx={{ flexShrink: 0 }}
        >
          登入
        </Button>
      ) : (
        <ActionTooltip title="開啟 AI 助手">
          <IconButton
            size="small"
            aria-label="開啟 AI 助手"
            disabled={!ready}
            onClick={(e) => {
              e.stopPropagation();
              onActivate();
            }}
            sx={{ color: accent, flexShrink: 0 }}
          >
            <OpenInFullIcon fontSize="small" />
          </IconButton>
        </ActionTooltip>
      )}
    </Box>
  );
}
