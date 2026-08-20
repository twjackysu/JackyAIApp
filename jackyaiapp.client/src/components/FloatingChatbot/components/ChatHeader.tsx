import AddCommentIcon from '@mui/icons-material/AddComment';
import CloseIcon from '@mui/icons-material/Close';
import HistoryIcon from '@mui/icons-material/History';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import { AppBar, Avatar, Badge, Box, IconButton, Stack, Toolbar, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { ActionTooltip } from '@/components/hints';

interface ChatHeaderProps {
  statusText: string;
  statusColor: string;
  onClose: () => void;
  onOpenHistory: () => void;
  onNewChat: () => void;
}

const ChatHeader = ({
  statusText,
  statusColor,
  onClose,
  onOpenHistory,
  onNewChat,
}: ChatHeaderProps) => {
  const theme = useTheme();

  return (
    <AppBar
      position="static"
      color="inherit"
      elevation={0}
      sx={{
        borderBottom: '1px solid',
        borderColor: 'divider',
        backgroundImage:
          theme.palette.mode === 'dark'
            ? 'linear-gradient(135deg, rgba(46,125,50,0.18), rgba(25,118,210,0.10))'
            : 'linear-gradient(135deg, rgba(46,125,50,0.10), rgba(25,118,210,0.06))',
      }}
    >
      <Toolbar>
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
          <Avatar sx={{ mr: 2, bgcolor: theme.palette.success.main, width: 42, height: 42 }}>
            <SmartToyIcon />
          </Avatar>
        </Badge>
        <Stack>
          <Typography variant="h6" component="div" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
            AI 助手
          </Typography>
          <Typography variant="caption" color="text.secondary">
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
        <IconButton onClick={onClose} color="inherit" aria-label="關閉">
          <CloseIcon />
        </IconButton>
      </Toolbar>
    </AppBar>
  );
};

export default ChatHeader;
