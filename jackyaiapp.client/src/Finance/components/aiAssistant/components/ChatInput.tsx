import AddIcon from '@mui/icons-material/Add';
import SendIcon from '@mui/icons-material/Send';
import { Box, IconButton, TextField, Tooltip, Typography, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useRef } from 'react';

import { PendingAttachment } from '../types';

import AttachmentChip from './AttachmentChip';

import { ATTACHMENT_ACCEPT, MAX_ATTACHMENTS } from '@/apis/chatbotApis/chatErrors';

interface ChatInputProps {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  onKeyPress: (e: React.KeyboardEvent) => void;
  disabled: boolean;
  inputRef?: React.Ref<HTMLTextAreaElement>;
  /** Files picked for the next message. */
  attachments: PendingAttachment[];
  onAddFiles: (files: File[]) => void;
  onRemoveAttachment: (key: string) => void;
}

const ChatInput = ({
  value,
  onChange,
  onSend,
  onKeyPress,
  disabled,
  inputRef,
  attachments,
  onAddFiles,
  onRemoveAttachment,
}: ChatInputProps) => {
  const theme = useTheme();
  const narrow = useMediaQuery(theme.breakpoints.down('sm'));
  const fileInputRef = useRef<HTMLInputElement>(null);
  const uploading = attachments.some((a) => !a.attachment && !a.error);
  const canSend = Boolean(value.trim()) && !disabled && !uploading;
  const failures = attachments.filter((a) => a.error);
  const canAttach = !disabled && attachments.length - failures.length < MAX_ATTACHMENTS;

  return (
    <Box
      sx={{
        px: { xs: 1.5, sm: 2 },
        pt: { xs: 1.5, sm: 2 },
        pb: 1,
        borderTop: '1px solid',
        borderColor: 'divider',
        backgroundColor: theme.palette.background.paper,
      }}
    >
      {attachments.length > 0 && (
        <Box sx={{ mb: 1 }}>
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75 }}>
            {attachments.map((a) => (
              <AttachmentChip
                key={a.key}
                fileName={a.fileName}
                kind={a.attachment?.kind}
                uploading={!a.attachment && !a.error}
                failed={Boolean(a.error)}
                truncated={a.attachment?.truncated}
                onDelete={disabled ? undefined : () => onRemoveAttachment(a.key)}
              />
            ))}
          </Box>
          {failures.map((a) => (
            <Typography
              key={a.key}
              variant="caption"
              component="div"
              sx={{ color: 'error.main', mt: 0.5 }}
            >
              {a.fileName}：{a.error}
            </Typography>
          ))}
        </Box>
      )}
      <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 1 }}>
        <input
          ref={fileInputRef}
          type="file"
          accept={ATTACHMENT_ACCEPT}
          multiple
          hidden
          data-testid="chat-attachment-input"
          onChange={(e) => {
            const files = Array.from(e.target.files ?? []);
            // Cleared so picking the same file again still fires a change.
            e.target.value = '';
            if (files.length > 0) onAddFiles(files);
          }}
        />
        <Tooltip title="附加檔案（Word、Excel、CSV、文字檔）">
          <span>
            <IconButton
              aria-label="附加檔案"
              onClick={() => fileInputRef.current?.click()}
              disabled={!canAttach}
              sx={{ width: 44, height: 44 }}
            >
              <AddIcon />
            </IconButton>
          </span>
        </Tooltip>
        <TextField
          fullWidth
          multiline
          maxRows={5}
          variant="outlined"
          // On a phone the hint about keys would wrap the box to three lines (and there is no Shift).
          placeholder={narrow ? '輸入訊息…' : '輸入訊息…（Enter 送出，Shift+Enter 換行）'}
          value={value}
          inputRef={inputRef}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={onKeyPress}
          disabled={disabled}
          sx={{
            '& .MuiOutlinedInput-root': {
              borderRadius: 3,
              backgroundColor:
                theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.04)' : 'rgba(0,0,0,0.02)',
            },
          }}
        />
        <IconButton
          color="primary"
          aria-label="送出"
          onClick={onSend}
          disabled={!canSend}
          sx={{
            bgcolor: canSend ? 'primary.main' : 'action.disabledBackground',
            color: canSend ? 'primary.contrastText' : 'action.disabled',
            width: 44,
            height: 44,
            '&:hover': { bgcolor: 'primary.dark' },
            '&.Mui-disabled': { color: 'action.disabled' },
          }}
        >
          <SendIcon fontSize="small" />
        </IconButton>
      </Box>
    </Box>
  );
};

export default ChatInput;
