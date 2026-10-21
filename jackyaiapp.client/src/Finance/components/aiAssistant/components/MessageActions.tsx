import CheckIcon from '@mui/icons-material/Check';
import ContentCopyIcon from '@mui/icons-material/ContentCopyOutlined';
import EditIcon from '@mui/icons-material/EditOutlined';
import { Box, IconButton, Tooltip } from '@mui/material';
import { useEffect, useState } from 'react';

import { copyText } from '../utils/clipboard';

/** How long 已複製 shows before the copy button goes back to normal. */
const COPIED_MS = 2000;

interface MessageActionsProps {
  /** What the copy button copies: the message as written (Markdown and all). */
  text: string;
  /** Present for the reader's own messages that can be edited now. */
  onEdit?: () => void;
  align: 'left' | 'right';
}

/**
 * The small buttons under a message. On a screen with a mouse they show while the pointer is over
 * the message (or a button has keyboard focus); where there is no hover — phones — they are always
 * shown, as ChatGPT does, since there is nothing to hover with.
 */
function MessageActions({ text, onEdit, align }: MessageActionsProps) {
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
