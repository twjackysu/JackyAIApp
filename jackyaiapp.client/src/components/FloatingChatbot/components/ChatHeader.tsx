import CloseIcon from '@mui/icons-material/Close';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import { AppBar, Avatar, Box, IconButton, Stack, Toolbar, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

interface ChatHeaderProps {
  statusText: string;
  statusColor: string;
  onClose: () => void;
}

const ChatHeader = ({ statusText, statusColor, onClose }: ChatHeaderProps) => {
  const theme = useTheme();

  return (
    <AppBar
      position="static"
      color="inherit"
      elevation={0}
      sx={{ borderBottom: '1px solid', borderColor: 'divider' }}
    >
      <Toolbar>
        <Avatar
          sx={{
            mr: 2,
            bgcolor: theme.palette.success.main,
            width: 40,
            height: 40,
          }}
        >
          <SmartToyIcon />
        </Avatar>
        <Stack>
          <Typography variant="h6" component="div">
            AI 助手
          </Typography>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
            <Box
              sx={{
                width: 8,
                height: 8,
                borderRadius: '50%',
                bgcolor: statusColor,
              }}
            />
            <Typography variant="caption" color="text.secondary">
              {statusText}
            </Typography>
          </Box>
        </Stack>
        <Box sx={{ flexGrow: 1 }} />
        <IconButton color="inherit">
          <MoreVertIcon />
        </IconButton>
        <IconButton onClick={onClose} color="inherit">
          <CloseIcon />
        </IconButton>
      </Toolbar>
    </AppBar>
  );
};

export default ChatHeader;
