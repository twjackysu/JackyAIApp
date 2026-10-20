import AddCommentIcon from '@mui/icons-material/AddComment';
import HistoryIcon from '@mui/icons-material/History';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import { Avatar, Badge, Box, IconButton, Stack, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { ActionTooltip } from '@/components/hints';

interface ChatHeaderProps {
  statusText: string;
  statusColor: string;
  onOpenHistory: () => void;
  onNewChat: () => void;
}

const ChatHeader = ({ statusText, statusColor, onOpenHistory, onNewChat }: ChatHeaderProps) => {
  const theme = useTheme();

  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1.5,
        px: { xs: 1.5, sm: 2 },
        py: 1,
        borderBottom: '1px solid',
        borderColor: 'divider',
        backgroundImage:
          theme.palette.mode === 'dark'
            ? 'linear-gradient(135deg, rgba(46,125,50,0.18), rgba(25,118,210,0.10))'
            : 'linear-gradient(135deg, rgba(46,125,50,0.10), rgba(25,118,210,0.06))',
      }}
    >
      <Badge
        overlap="circular"
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        badgeContent={
          <Box
            sx={{
              width: 11,
              height: 11,
              borderRadius: '50%',
              bgcolor: statusColor,
              border: `2px solid ${theme.palette.background.paper}`,
            }}
          />
        }
      >
        <Avatar sx={{ bgcolor: theme.palette.success.main, width: 40, height: 40 }}>
          <SmartToyIcon />
        </Avatar>
      </Badge>
      <Stack sx={{ minWidth: 0 }}>
        <Typography variant="subtitle1" component="div" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
          AI 助手
        </Typography>
        <Typography variant="caption" color="text.secondary" noWrap>
          {statusText}
        </Typography>
      </Stack>
      <Box sx={{ flexGrow: 1 }} />
      <ActionTooltip title="對話紀錄">
        <IconButton onClick={onOpenHistory} color="inherit" aria-label="對話紀錄">
          <HistoryIcon />
        </IconButton>
      </ActionTooltip>
      <ActionTooltip title="新對話">
        <IconButton onClick={onNewChat} color="inherit" aria-label="新對話">
          <AddCommentIcon />
        </IconButton>
      </ActionTooltip>
    </Box>
  );
};

export default ChatHeader;
