import EditNoteIcon from '@mui/icons-material/EditNote';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ViewSidebarIcon from '@mui/icons-material/ViewSidebarOutlined';
import { Box, IconButton, LinearProgress, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { ACCENT, hairline } from '../style';

import AssistantMark from './AssistantMark';
import { HISTORY_COLUMN_BREAKPOINT } from './historyColumn';

import { ActionTooltip } from '@/components/hints';

interface ChatHeaderProps {
  /** What the assistant is doing, while it answers. */
  statusText: string;
  /** A reply is being written: starting over has to wait. */
  busy: boolean;
  onOpenHistory: () => void;
  onNewChat: () => void;
  /** Folds the panel back into its one-line box (the conversation is kept). */
  onCollapse: () => void;
}

/**
 * A slim bar: the assistant's mark and name, what it is doing, and the panel's actions. While an
 * answer is written, a thin line runs along its lower edge instead of a separate progress row.
 */
const ChatHeader = ({
  statusText,
  busy,
  onOpenHistory,
  onNewChat,
  onCollapse,
}: ChatHeaderProps) => {
  const theme = useTheme();

  return (
    <Box
      sx={{
        position: 'relative',
        display: 'flex',
        alignItems: 'center',
        gap: 1.25,
        px: { xs: 1.5, sm: 2 },
        minHeight: 56,
        borderBottom: `1px solid ${hairline(theme)}`,
      }}
    >
      {/* Only where the history is not already shown beside the chat (see HISTORY_COLUMN_BREAKPOINT) */}
      <ActionTooltip title="對話紀錄">
        <IconButton
          onClick={onOpenHistory}
          aria-label="對話紀錄"
          size="small"
          sx={{ display: { [HISTORY_COLUMN_BREAKPOINT]: 'none' }, color: 'text.secondary' }}
        >
          <ViewSidebarIcon fontSize="small" />
        </IconButton>
      </ActionTooltip>
      <AssistantMark size={30} />
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="subtitle2" component="div" sx={{ fontWeight: 700, lineHeight: 1.25 }}>
          AI 助手
        </Typography>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, minWidth: 0 }}>
          <Box
            sx={{
              width: 6,
              height: 6,
              borderRadius: '50%',
              flexShrink: 0,
              bgcolor: busy ? 'info.main' : ACCENT,
              boxShadow: busy ? undefined : `0 0 0 3px ${ACCENT}22`,
            }}
          />
          <Typography variant="caption" color="text.secondary" noWrap>
            {busy ? statusText || '處理中…' : '台股資料已連線'}
          </Typography>
        </Box>
      </Box>
      <Box sx={{ flexGrow: 1 }} />
      <ActionTooltip title="新對話">
        <span>
          <IconButton
            onClick={onNewChat}
            aria-label="新對話"
            disabled={busy}
            size="small"
            sx={{ color: 'text.secondary' }}
          >
            <EditNoteIcon fontSize="small" />
          </IconButton>
        </span>
      </ActionTooltip>
      <ActionTooltip title="收合">
        <IconButton
          onClick={onCollapse}
          aria-label="收合"
          size="small"
          sx={{ color: 'text.secondary' }}
        >
          <ExpandLessIcon fontSize="small" />
        </IconButton>
      </ActionTooltip>
      {busy && (
        <LinearProgress
          sx={{
            position: 'absolute',
            left: 0,
            right: 0,
            bottom: -1,
            height: 2,
            bgcolor: 'transparent',
            '& .MuiLinearProgress-bar': {
              backgroundImage: `linear-gradient(90deg, ${ACCENT}, #818cf8)`,
            },
          }}
        />
      )}
    </Box>
  );
};

export default ChatHeader;
