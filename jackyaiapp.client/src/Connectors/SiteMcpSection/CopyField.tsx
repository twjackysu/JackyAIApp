import { Check, ContentCopy } from '@mui/icons-material';
import { Box, IconButton } from '@mui/material';
import { useState } from 'react';

import { copyText } from '@/components/aiChat/utils/clipboard';
import { ActionTooltip } from '@/components/hints';

interface CopyFieldProps {
  /** What the reader copies, shown as it is. */
  text: string;
  /** What the copy button is called ("複製網址"). */
  label: string;
  /** Keep line breaks (a config file) rather than one line. */
  block?: boolean;
}

/** Text the reader is meant to copy as it is (a URL, a token, a config), with a button that does. */
export default function CopyField({ text, label, block = false }: CopyFieldProps) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    await copyText(text);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1500);
  };
  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: block ? 'flex-start' : 'center',
        gap: 1,
        p: 1,
        pl: 1.5,
        borderRadius: 1,
        bgcolor: 'action.hover',
        border: 1,
        borderColor: 'divider',
      }}
    >
      <Box
        component={block ? 'pre' : 'code'}
        sx={{
          flex: 1,
          m: 0,
          fontFamily: 'monospace',
          fontSize: 13,
          overflowX: 'auto',
          whiteSpace: block ? 'pre' : 'nowrap',
          py: 0.5,
        }}
      >
        {text}
      </Box>
      <ActionTooltip title={copied ? '已複製' : label}>
        <IconButton size="small" onClick={copy} aria-label={label}>
          {copied ? <Check fontSize="small" color="success" /> : <ContentCopy fontSize="small" />}
        </IconButton>
      </ActionTooltip>
    </Box>
  );
}
