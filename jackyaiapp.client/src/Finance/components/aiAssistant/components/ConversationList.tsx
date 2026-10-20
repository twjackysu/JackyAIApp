import AddCommentIcon from '@mui/icons-material/AddComment';
import DeleteOutlineIcon from '@mui/icons-material/Delete';
import {
  Box,
  Button,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  Typography,
} from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { useDeleteConversationMutation, useGetConversationsQuery } from '@/apis/chatbotApis';
import CenteredSpinner from '@/components/CenteredSpinner';

interface ConversationListProps {
  activeConversationId: string;
  onSelect: (conversationId: string) => void;
  onNewChat: () => void;
  /** A reply is being written: switching away from it, or starting over, has to wait. */
  busy?: boolean;
  /** Called after a pick or a new chat, for a list shown in a drawer that should close then. */
  onDone?: () => void;
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

/** The signed-in user's conversations, newest first — the body of the history column. */
const ConversationList = ({
  activeConversationId,
  onSelect,
  onNewChat,
  busy = false,
  onDone,
}: ConversationListProps) => {
  const theme = useTheme();
  const { data, isFetching } = useGetConversationsQuery(undefined, {
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
    <Box sx={{ p: 2, display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
      <Typography variant="subtitle1" sx={{ fontWeight: 700, mb: 1.5 }}>
        對話紀錄
      </Typography>

      <Button
        fullWidth
        variant="contained"
        disableElevation
        disabled={busy}
        startIcon={<AddCommentIcon />}
        onClick={() => {
          onNewChat();
          onDone?.();
        }}
        sx={{ textTransform: 'none', borderRadius: 2, mb: 1.5 }}
      >
        新對話
      </Button>

      {isFetching && conversations.length === 0 ? (
        <CenteredSpinner size={24} sx={{ py: 4 }} />
      ) : conversations.length === 0 ? (
        <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>
          還沒有任何對話
        </Typography>
      ) : (
        <List sx={{ flex: 1, minHeight: 0, overflowY: 'auto', mx: -1 }}>
          {conversations.map((c) => {
            const active = c.id === activeConversationId;
            return (
              <ListItemButton
                key={c.id}
                selected={active}
                disabled={busy && !active}
                onClick={() => {
                  onSelect(c.id);
                  onDone?.();
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
                  disabled={isDeleting || busy}
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
  );
};

export default ConversationList;
