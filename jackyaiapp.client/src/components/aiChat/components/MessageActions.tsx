import CheckIcon from '@mui/icons-material/Check';
import ContentCopyIcon from '@mui/icons-material/ContentCopyOutlined';
import EditIcon from '@mui/icons-material/EditOutlined';
import StopIcon from '@mui/icons-material/StopCircleOutlined';
import VolumeUpIcon from '@mui/icons-material/VolumeUpOutlined';
import { Box, IconButton, Tooltip } from '@mui/material';
import { useEffect, useState } from 'react';

import { useSpeechOutput } from '../hooks/useSpeechOutput';
import { copyText } from '../utils/clipboard';

/** How long 已複製 shows before the copy button goes back to normal. */
const COPIED_MS = 2000;

interface MessageActionsProps {
  /** What the copy button copies: the message as written (Markdown and all). */
  text: string;
  /** Present for the reader's own messages that can be edited now. */
  onEdit?: () => void;
  /** An answer: it can be read aloud (where the browser can speak). */
  readAloud?: boolean;
  /** Read it aloud by itself, once for this key (an answer asked to be read once done, D-120). */
  autoReadKey?: string;
  align: 'left' | 'right';
}

/** Reads the answer aloud, or stops (`useSpeechOutput`); nothing where the browser cannot speak. */
function ReadAloudButton({
  text,
  autoReadKey,
  sx,
}: {
  text: string;
  autoReadKey?: string;
  sx: object;
}) {
  const { supported, speaking, toggle } = useSpeechOutput(text, autoReadKey);
  if (!supported) return null;
  const label = speaking ? '停止朗讀' : '朗讀';
  return (
    <Tooltip title={label}>
      <IconButton size="small" aria-label={label} aria-pressed={speaking} onClick={toggle} sx={sx}>
        {speaking ? <StopIcon sx={{ fontSize: 16 }} /> : <VolumeUpIcon sx={{ fontSize: 16 }} />}
      </IconButton>
    </Tooltip>
  );
}

/**
 * The small buttons under a message. On a screen with a mouse they show while the pointer is over
 * the message (or a button has keyboard focus); where there is no hover — phones — they are always
 * shown, as ChatGPT does, since there is nothing to hover with. While an answer is read aloud they stay shown, so
 * its stop button is there to press (`style.ts`).
 */
function MessageActions({ text, onEdit, readAloud, autoReadKey, align }: MessageActionsProps) {
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return;
    const timer = setTimeout(() => setCopied(false), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copied]);

  const buttonSx = { color: 'text.secondary', '&:hover': { color: 'text.primary' } };

  return (
    <Box
      className="message-actions"
      sx={{
        display: 'flex',
        gap: 0.25,
        mt: 0.5,
        justifyContent: align === 'right' ? 'flex-end' : 'flex-start',
      }}
    >
      <Tooltip title={copied ? '已複製' : '複製'}>
        <IconButton
          size="small"
          aria-label={copied ? '已複製' : '複製'}
          onClick={async () => {
            await copyText(text);
            setCopied(true);
          }}
          sx={buttonSx}
        >
          {copied ? <CheckIcon sx={{ fontSize: 16 }} /> : <ContentCopyIcon sx={{ fontSize: 16 }} />}
        </IconButton>
      </Tooltip>
      {readAloud && <ReadAloudButton text={text} autoReadKey={autoReadKey} sx={buttonSx} />}
      {onEdit && (
        <Tooltip title="編輯">
          <IconButton size="small" aria-label="編輯" onClick={onEdit} sx={buttonSx}>
            <EditIcon sx={{ fontSize: 16 }} />
          </IconButton>
        </Tooltip>
      )}
    </Box>
  );
}

export default MessageActions;
