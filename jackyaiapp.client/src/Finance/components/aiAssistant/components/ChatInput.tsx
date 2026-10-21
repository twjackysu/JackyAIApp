import AddIcon from '@mui/icons-material/Add';
import AttachFileIcon from '@mui/icons-material/AttachFile';
import CheckIcon from '@mui/icons-material/Check';
import CloseIcon from '@mui/icons-material/Close';
import ScheduleIcon from '@mui/icons-material/Schedule';
import SendIcon from '@mui/icons-material/Send';
import StopIcon from '@mui/icons-material/Stop';
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

import { PendingAttachment, QueuedMessage } from '../types';

import AttachmentChip from './AttachmentChip';

import { ATTACHMENT_ACCEPT, MAX_ATTACHMENTS } from '@/apis/chatbotApis/chatErrors';
import { attachmentContentUrl } from '@/apis/chatbotApis/types';

// Four controls share a phone-wide row while an answer is written (+, the box, stop, send): smaller
// buttons there leave the box room to type in.
const BUTTON_SIZE = { width: { xs: 36, sm: 44 }, height: { xs: 36, sm: 44 }, flexShrink: 0 };

interface ChatInputProps {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  onKeyPress: (e: React.KeyboardEvent) => void;
  /** An answer is being written: the box still takes messages (they wait their turn). */
  busy: boolean;
  /** Stops the answer being written. */
  onStop: () => void;
  /** Messages sent while the answer is being written, in the order they will go. */
  queued: QueuedMessage[];
  onRemoveQueued: (key: string) => void;
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
  busy,
  onStop,
  queued,
  onRemoveQueued,
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
  const canSend = Boolean(value.trim()) && !uploading;
  const failures = attachments.filter((a) => a.error);
  const canAttach = attachments.length - failures.length < MAX_ATTACHMENTS;

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
      {queued.length > 0 && (
        <Box sx={{ mb: 1, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
          {queued.map((q) => (
            <Box
              key={q.key}
              data-testid="queued-message"
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 1,
                pl: 1.25,
                pr: 0.5,
                py: 0.25,
                borderRadius: 2,
                border: '1px dashed',
                borderColor: 'divider',
              }}
            >
              <ScheduleIcon fontSize="small" sx={{ color: 'text.secondary' }} />
              <Typography variant="caption" sx={{ color: 'text.secondary', flexShrink: 0 }}>
                排隊中
              </Typography>
              <Typography variant="body2" noWrap sx={{ flex: 1, minWidth: 0 }}>
                {q.text}
                {q.attachments.length > 0 && `（附 ${q.attachments.length} 個檔案）`}
              </Typography>
              <IconButton size="small" aria-label="取消排隊" onClick={() => onRemoveQueued(q.key)}>
                <CloseIcon fontSize="small" />
              </IconButton>
            </Box>
          ))}
        </Box>
      )}
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
                onDelete={() => onRemoveAttachment(a.key)}
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
              sx={BUTTON_SIZE}
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
          placeholder={
            busy
              ? narrow
                ? '下一則…'
                : '回覆中，可以先輸入下一則（回覆完自動送出）'
              : narrow
                ? '輸入訊息…'
                : '輸入訊息…（Enter 送出，Shift+Enter 換行）'
          }
          value={value}
          inputRef={inputRef}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={onKeyPress}
          onPaste={onPaste}
          sx={{
            '& .MuiOutlinedInput-root': {
              borderRadius: 3,
              backgroundColor:
                theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.04)' : 'rgba(0,0,0,0.02)',
            },
          }}
        />
        {busy && (
          <Tooltip title="停止回覆（已寫出的部分會保留）">
            <IconButton
              aria-label="停止"
              onClick={onStop}
              sx={{
                ...BUTTON_SIZE,
                border: '1px solid',
                borderColor: 'divider',
              }}
            >
              <StopIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        )}
        {/* While an answer is written, sending only queues, and only once there is something to
            send: until then stop stands alone and the box keeps the room. */}
        {(!busy || canSend) && (
          <IconButton
            color="primary"
            aria-label={busy ? '排入佇列' : '送出'}
            onClick={onSend}
            disabled={!canSend}
            sx={{
              bgcolor: canSend ? 'primary.main' : 'action.disabledBackground',
              color: canSend ? 'primary.contrastText' : 'action.disabled',
              ...BUTTON_SIZE,
              '&:hover': { bgcolor: 'primary.dark' },
              '&.Mui-disabled': { color: 'action.disabled' },
            }}
          >
            <SendIcon fontSize="small" />
          </IconButton>
        )}
      </Box>
    </Box>
  );
};

export default ChatInput;
