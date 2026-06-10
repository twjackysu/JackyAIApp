import AddCommentIcon from '@mui/icons-material/AddComment';
import DeleteOutlineIcon from '@mui/icons-material/Delete';
import {
  Box,
  Button,
  CircularProgress,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  Typography,
} from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { useDeleteConversationMutation, useGetConversationsQuery } from '@/apis/chatbotApis';

interface ConversationDrawerProps {
  open: boolean;
  onClose: () => void;
  activeConversationId: string;
  onSelect: (conversationId: string) => void;
  onNewChat: () => void;
}

const formatTime = (epochSeconds: number) => {
  if (!epochSeconds) return '';
  const d = new Date(epochSeconds * 1000);
  const now = new Date();
  const sameDay = d.toDateString() === now.toDateString();
  return sameDay
    ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString([], { month: 'short', day: 'numeric' });
};

const ConversationDrawer = ({
  open,
  onClose,
  activeConversationId,
  onSelect,
  onNewChat,
}: ConversationDrawerProps) => {
  const theme = useTheme();
  const { data, isFetching } = useGetConversationsQuery(undefined, {
    skip: !open,
    refetchOnMountOrArgChange: true,
  });
  const [deleteConversation, { isLoading: isDeleting }] = useDeleteConversationMutation();

  const conversations = data?.data ?? [];

  const handleDelete = async (e: React.MouseEvent, id: string) => {
    e.stopPropagation();
    await deleteConversation(id);
    if (id === activeConversationId) onNewChat();
  };

  return (
    <Drawer
      anchor="left"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '85vw', sm: 320 } } } }}
    >
      <Box sx={{ p: 2, display: 'flex', flexDirection: 'column', height: '100%' }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 700, mb: 1.5 }}>
          對話紀錄
        </Typography>

        <Button
          fullWidth
          variant="contained"
          disableElevation
          startIcon={<AddCommentIcon />}
          onClick={() => {
            onNewChat();
            onClose();
          }}
          sx={{ textTransform: 'none', borderRadius: 2, mb: 1.5 }}
        >
          新對話
        </Button>

        {isFetching && conversations.length === 0 ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
            <CircularProgress size={24} />
          </Box>
        ) : conversations.length === 0 ? (
          <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>
            還沒有任何對話
          </Typography>
        ) : (
          <List sx={{ flex: 1, overflowY: 'auto', mx: -1 }}>
            {conversations.map((c) => {
              const active = c.id === activeConversationId;
              return (
                <ListItemButton
                  key={c.id}
                  selected={active}
                  onClick={() => {
                    onSelect(c.id);
                    onClose();
                  }}
                  sx={{
                    borderRadius: 2,
                    mb: 0.5,
                    '&.Mui-selected': {
                      bgcolor:
                        theme.palette.mode === 'dark'
                          ? 'rgba(46,125,50,0.20)'
                          : 'rgba(46,125,50,0.10)',
                    },
                  }}
                >
                  <ListItemText
                    primary={c.name}
                    secondary={formatTime(c.updatedAt)}
                    slotProps={{
                      primary: { noWrap: true, sx: { fontWeight: active ? 700 : 400 } },
                    }}
                  />
                  <IconButton
                    edge="end"
                    size="small"
                    disabled={isDeleting}
                    onClick={(e) => handleDelete(e, c.id)}
                    aria-label="刪除對話"
                  >
                    <DeleteOutlineIcon fontSize="small" />
                  </IconButton>
                </ListItemButton>
              );
            })}
          </List>
        )}
      </Box>
    </Drawer>
  );
};

export default ConversationDrawer;
