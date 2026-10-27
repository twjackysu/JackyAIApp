import OpenInFullIcon from '@mui/icons-material/OpenInFull';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import InputBase from '@mui/material/InputBase';
import { alpha, useTheme } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { type Ref, useEffect, useRef, useState } from 'react';

import { useAiChatConfig } from '../config';
import { ACCENT, ACCENT_GRADIENT, panelSurface } from '../style';

import AssistantMark from './AssistantMark';

import { ActionTooltip } from '@/components/hints';
import { signInAndComeBack } from '@/utils/signIn';

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
  /** The 開啟 button, which gets the focus when the panel folds back. */
  openButtonRef?: Ref<HTMLButtonElement>;
}

/** How long each example question stays in the idle box before the next one. */
export const EXAMPLE_ROTATE_MS = 4000;

/** The box is idle: nothing typed, nothing going on — it shows example questions in turn. */
const isIdle = ({ state, busy, hasConversation, value }: CompactChatBarProps) =>
  state === 'ready' && !busy && !hasConversation && !value;

const placeholderFor = (
  { state, busy, hasConversation }: CompactChatBarProps,
  example: string,
  signedOut: string,
) => {
  if (state === 'signedOut') return signedOut;
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
  // The ref apart: what the box shows is read from the rest.
  const { openButtonRef, ...shown } = props;
  const { value, onChange, onActivate, state, busy } = shown;
  const theme = useTheme();
  const inputRef = useRef<HTMLInputElement>(null);
  const ready = state === 'ready';
  const accent = ACCENT;
  const { examples, signedOutPlaceholder } = useAiChatConfig();

  // A different question every few seconds shows how much can be asked.
  const [exampleIndex, setExampleIndex] = useState(0);
  const idle = isIdle(shown);
  useEffect(() => {
    if (!idle) return;
    const timer = setInterval(
      () => setExampleIndex((i) => (i + 1) % examples.length),
      EXAMPLE_ROTATE_MS,
    );
    return () => clearInterval(timer);
  }, [idle, examples.length]);

  return (
    <Box
      // The whole strip is the target: a click anywhere on it lands in the input.
      onClick={() => inputRef.current?.focus()}
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1.25,
        minHeight: 52,
        pl: 1.25,
        pr: 1,
        // The same gradient outline as the open panel: one thing, folded or not.
        borderRadius: '16px',
        border: '1px solid transparent',
        background: [
          `radial-gradient(400px circle at 0% 50%, ${alpha(ACCENT, 0.08)}, transparent 70%) padding-box`,
          `linear-gradient(${panelSurface(theme)}, ${panelSurface(theme)}) padding-box`,
          `${ACCENT_GRADIENT} border-box`,
        ].join(', '),
        boxShadow: '0 16px 40px -28px rgba(52, 211, 153, 0.6)',
        cursor: ready ? 'text' : 'default',
        transition: 'box-shadow 140ms',
        '&:hover': ready ? { boxShadow: '0 16px 40px -20px rgba(52, 211, 153, 0.7)' } : undefined,
      }}
    >
      <AssistantMark size={30} />
      <Typography
        variant="subtitle2"
        sx={{ fontWeight: 700, flexShrink: 0, display: { xs: 'none', sm: 'block' } }}
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
        placeholder={placeholderFor(
          shown,
          examples[exampleIndex % examples.length],
          signedOutPlaceholder,
        )}
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
            ref={openButtonRef}
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
