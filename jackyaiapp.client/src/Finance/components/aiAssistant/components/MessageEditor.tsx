import { Box, Button, InputBase, Typography } from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { useState } from 'react';

import { ACCENT, hairline } from '../style';

interface MessageEditorProps {
  initial: string;
  /** The message has files, which go again with the new wording. */
  attachmentCount: number;
  onCancel: () => void;
  onSubmit: (text: string) => void;
}

/**
 * Editing a message in place, where its bubble was. Sending it replaces the message and everything
 * after it — said under the box, since the later answers disappear.
 */
function MessageEditor({ initial, attachmentCount, onCancel, onSubmit }: MessageEditorProps) {
  const theme = useTheme();
  const [text, setText] = useState(initial);
  const canSubmit = Boolean(text.trim()) && text !== initial;

  return (
    <Box
      sx={{
        width: '100%',
        borderRadius: '18px',
        border: `1px solid ${alpha(ACCENT, 0.5)}`,
        boxShadow: `0 0 0 3px ${alpha(ACCENT, 0.1)}`,
        bgcolor: theme.palette.mode === 'dark' ? '#1b1f26' : theme.palette.background.paper,
        p: 1.5,
      }}
    >
      <InputBase
        fullWidth
        multiline
        maxRows={10}
        autoFocus
        value={text}
        onChange={(e) => setText(e.target.value)}
        onFocus={(e) => {
          // The cursor at the end of the text, not the start.
          const el = e.target as HTMLTextAreaElement;
          el.setSelectionRange(el.value.length, el.value.length);
        }}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            e.preventDefault();
            onCancel();
          } else if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
            e.preventDefault();
            if (canSubmit) onSubmit(text.trim());
          }
        }}
        inputProps={{ 'aria-label': '編輯訊息' }}
        sx={{ fontSize: '0.95rem', lineHeight: 1.6 }}
      />
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1, flexWrap: 'wrap' }}>
        <Typography variant="caption" color="text.secondary" sx={{ flex: 1, minWidth: 160 }}>
          送出後，這則之後的對話會重新產生
          {attachmentCount > 0 && `，附件（${attachmentCount} 個）會一併送出`}
        </Typography>
        <Button
          size="small"
          onClick={onCancel}
          sx={{ borderRadius: 5, color: 'text.secondary', border: `1px solid ${hairline(theme)}` }}
        >
          取消
        </Button>
        <Button
          size="small"
          variant="contained"
          disableElevation
          disabled={!canSubmit}
          onClick={() => onSubmit(text.trim())}
          sx={{
            borderRadius: 5,
            bgcolor: ACCENT,
            color: '#0b1210',
            '&:hover': { bgcolor: '#2fbf87' },
          }}
        >
          送出
        </Button>
      </Box>
    </Box>
  );
}

export default MessageEditor;
