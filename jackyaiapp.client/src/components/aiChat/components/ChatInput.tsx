import AddIcon from '@mui/icons-material/Add';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import AttachFileIcon from '@mui/icons-material/AttachFile';
import CheckIcon from '@mui/icons-material/Check';
import CloseIcon from '@mui/icons-material/Close';
import ScheduleIcon from '@mui/icons-material/Schedule';
import StopRoundedIcon from '@mui/icons-material/StopRounded';
import {
  Box,
  IconButton,
  InputBase,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Tooltip,
  Typography,
  useMediaQuery,
} from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { useRef, useState } from 'react';

import { ACCENT, ACCENT_GRADIENT, hairline } from '../style';
import { PendingAttachment, QueuedMessage } from '../types';

import AttachmentChip from './AttachmentChip';

import { ATTACHMENT_ACCEPT, MAX_ATTACHMENTS } from '@/apis/chatbotApis/chatErrors';
import { attachmentContentUrl } from '@/apis/chatbotApis/types';

/** The composer lines up with the conversation column above it. */
const COLUMN_MAX_WIDTH = 780;

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

/**
 * The composer: one rounded box holding the files to send, the message and its buttons — attach
 * on the left, stop and send on the right. Messages waiting their turn are listed above it.
 */
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
  const dark = theme.palette.mode === 'dark';

  return (
    <Box sx={{ px: { xs: 1.5, sm: 3 }, pt: 1, pb: 1.25 }}>
      <Box sx={{ maxWidth: COLUMN_MAX_WIDTH, mx: 'auto' }}>
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
                  pl: 1.5,
                  pr: 0.5,
                  py: 0.25,
                  borderRadius: 2.5,
                  border: `1px dashed ${hairline(theme)}`,
                  bgcolor: dark ? 'rgba(255,255,255,0.02)' : 'rgba(0,0,0,0.015)',
                }}
              >
                <ScheduleIcon sx={{ fontSize: 16, color: 'text.secondary' }} />
                <Typography variant="caption" sx={{ color: 'text.secondary', flexShrink: 0 }}>
                  排隊中
                </Typography>
                <Typography variant="body2" noWrap sx={{ flex: 1, minWidth: 0 }}>
                  {q.text}
                  {q.attachments.length > 0 && `（附 ${q.attachments.length} 個檔案）`}
                </Typography>
                <IconButton
                  size="small"
                  aria-label="取消排隊"
                  onClick={() => onRemoveQueued(q.key)}
                >
                  <CloseIcon sx={{ fontSize: 16 }} />
                </IconButton>
              </Box>
            ))}
          </Box>
        )}

        <Box
          sx={{
            borderRadius: '22px',
            border: `1px solid ${hairline(theme)}`,
            bgcolor: dark ? '#1b1f26' : theme.palette.background.paper,
            boxShadow: dark
              ? '0 8px 28px -12px rgba(0,0,0,0.7)'
              : '0 6px 20px -10px rgba(0,0,0,0.2)',
            transition: 'border-color 140ms, box-shadow 140ms',
            '&:focus-within': {
              borderColor: alpha(ACCENT, 0.55),
              boxShadow: `0 0 0 3px ${alpha(ACCENT, 0.12)}, 0 8px 28px -12px rgba(0,0,0,0.7)`,
            },
          }}
        >
          {attachments.length > 0 && (
            <Box sx={{ px: 1.5, pt: 1.25 }}>
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
                      a.attachment?.kind === 'pdf'
                        ? attachmentContentUrl(a.attachment.id)
                        : undefined
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

          <InputBase
            fullWidth
            multiline
            maxRows={8}
            // On a phone the hint about keys would not fit (and there is no Shift).
            placeholder={
              busy
                ? narrow
                  ? '先輸入下一則…'
                  : '回覆中，可以先輸入下一則（回覆完自動送出）'
                : narrow
                  ? '問台股、財報、ETF…'
                  : '問台股、財報、ETF、期貨…（Enter 送出，Shift+Enter 換行）'
            }
            value={value}
            inputRef={inputRef}
            onChange={(e) => onChange(e.target.value)}
            onKeyDown={onKeyPress}
            onPaste={onPaste}
            inputProps={{ 'aria-label': '訊息' }}
            sx={{ px: 2, pt: 1.5, pb: 0.5, fontSize: '0.95rem', lineHeight: 1.6 }}
          />

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, px: 1, pb: 1 }}>
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
              <IconButton
                aria-label="附加"
                aria-haspopup="menu"
                size="small"
                onClick={(e) => setMenuAnchor(e.currentTarget)}
                sx={{ border: `1px solid ${hairline(theme)}`, color: 'text.secondary' }}
              >
                <AddIcon fontSize="small" />
              </IconButton>
            </Tooltip>
            <Menu
              anchorEl={menuAnchor}
              open={Boolean(menuAnchor)}
              onClose={() => setMenuAnchor(null)}
              anchorOrigin={{ vertical: 'top', horizontal: 'left' }}
              transformOrigin={{ vertical: 'bottom', horizontal: 'left' }}
              slotProps={{ paper: { sx: { borderRadius: 3, mt: -1 } } }}
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

            <Box sx={{ flex: 1 }} />

            {busy && (
              <Tooltip title="停止回覆（已寫出的部分會保留）">
                <IconButton
                  aria-label="停止"
                  onClick={onStop}
                  size="small"
                  sx={{
                    width: 34,
                    height: 34,
                    bgcolor: dark ? 'rgba(255,255,255,0.9)' : 'grey.900',
                    color: dark ? 'grey.900' : 'common.white',
                    '&:hover': { bgcolor: dark ? 'common.white' : 'common.black' },
                  }}
                >
                  <StopRoundedIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            )}
            {/* While an answer is written, sending only queues, and only once there is something
                to send: until then stop stands alone. */}
            {(!busy || canSend) && (
              <IconButton
                aria-label={busy ? '排入佇列' : '送出'}
                onClick={onSend}
                disabled={!canSend}
                size="small"
                sx={{
                  width: 34,
                  height: 34,
                  color: '#0b1210',
                  backgroundImage: canSend ? ACCENT_GRADIENT : 'none',
                  bgcolor: canSend ? undefined : 'action.disabledBackground',
                  '&:hover': { filter: 'brightness(1.08)' },
                  '&.Mui-disabled': { color: 'action.disabled' },
                }}
              >
                <ArrowUpwardIcon fontSize="small" />
              </IconButton>
            )}
          </Box>
        </Box>

        <Typography
          variant="caption"
          component="p"
          sx={{ textAlign: 'center', color: 'text.disabled', mt: 0.75, mb: 0, fontSize: '0.7rem' }}
        >
          AI 回答可能有誤，數據請以官方資料為準，不構成投資建議。
        </Typography>
      </Box>
    </Box>
  );
};

export default ChatInput;
