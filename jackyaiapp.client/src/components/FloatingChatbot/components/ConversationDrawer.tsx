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
  /**
   * When provided, the drawer renders inside this element (the chat dialog's
   * paper) instead of the whole screen — so its backdrop dims only the chat
   * window rather than stacking a second full-screen backdrop over the dialog.
   */
  containerEl?: HTMLElement | null;
}

const formatTime = (isoDate: string) => {
  if (!isoDate) return '';
  const d = new Date(isoDate);
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
  containerEl,
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

  // When scoped to a container, position the modal/backdrop/paper absolutely so
  // they stay within the chat dialog instead of covering the whole viewport.
  const scoped = Boolean(containerEl);

  return (
    <Drawer
      anchor="left"
      open={open}
      onClose={onClose}
      variant="temporary"
      slotProps={{
        root: scoped ? { container: containerEl, style: { position: 'absolute' } } : undefined,
        backdrop: scoped ? { style: { position: 'absolute' } } : undefined,
        paper: {
          sx: {
            width: { xs: '85%', sm: 280 },
            ...(scoped ? { position: 'absolute' } : {}),
          },
        },
      }}
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
                    primary={c.title}
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
