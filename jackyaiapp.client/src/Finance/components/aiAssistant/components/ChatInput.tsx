import AddIcon from '@mui/icons-material/Add';
import AttachFileIcon from '@mui/icons-material/AttachFile';
import CheckIcon from '@mui/icons-material/Check';
import SendIcon from '@mui/icons-material/Send';
import {
  Box,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  TextField,
  Tooltip,
  Typography,
  useMediaQuery,
} from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useRef, useState } from 'react';

import { PendingAttachment } from '../types';

import AttachmentChip from './AttachmentChip';

import { ATTACHMENT_ACCEPT, MAX_ATTACHMENTS } from '@/apis/chatbotApis/chatErrors';
import { attachmentContentUrl } from '@/apis/chatbotApis/types';

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
  onPaste: (e: React.ClipboardEvent) => void;
  /** Pasted formatted text (tables, lists) is turned into Markdown rather than plain text. */
  keepFormatting: boolean;
  onKeepFormattingChange: (keep: boolean) => void;
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
  onPaste,
  keepFormatting,
  onKeepFormattingChange,
}: ChatInputProps) => {
  const theme = useTheme();
  const narrow = useMediaQuery(theme.breakpoints.down('sm'));
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
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
                pageCount={a.attachment?.pageCount}
                thumbnailUrl={a.previewUrl}
                href={
                  a.attachment?.kind === 'pdf' ? attachmentContentUrl(a.attachment.id) : undefined
                }
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
        <Tooltip title="附加檔案、貼上設定">
          <span>
            <IconButton
              aria-label="附加"
              aria-haspopup="menu"
              onClick={(e) => setMenuAnchor(e.currentTarget)}
              disabled={disabled}
              sx={{ width: 44, height: 44 }}
            >
              <AddIcon />
            </IconButton>
          </span>
        </Tooltip>
        <Menu
          anchorEl={menuAnchor}
          open={Boolean(menuAnchor)}
          onClose={() => setMenuAnchor(null)}
          anchorOrigin={{ vertical: 'top', horizontal: 'left' }}
          transformOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        >
          <MenuItem
            disabled={!canAttach}
            onClick={() => {
              setMenuAnchor(null);
              fileInputRef.current?.click();
            }}
          >
            <ListItemIcon>
              <AttachFileIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText
              primary="附加檔案"
              secondary={
                canAttach ? 'Word、Excel、CSV、文字檔、PDF、圖片' : `最多 ${MAX_ATTACHMENTS} 個`
              }
            />
          </MenuItem>
          <MenuItem
            role="menuitemcheckbox"
            aria-checked={keepFormatting}
            onClick={() => onKeepFormattingChange(!keepFormatting)}
          >
            <ListItemIcon>{keepFormatting && <CheckIcon fontSize="small" />}</ListItemIcon>
            <ListItemText
              primary="貼上時保留格式"
              secondary={keepFormatting ? '表格、清單轉成 Markdown' : '只貼上純文字'}
            />
          </MenuItem>
        </Menu>
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
          onPaste={onPaste}
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
