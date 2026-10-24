import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlineOutlined';
import EditNoteIcon from '@mui/icons-material/EditNote';
import { Box, Button, IconButton, List, ListItemButton, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { useAiChatConfig } from '../config';
import { ACCENT, accentWash, hairline } from '../style';
import { periodOf } from '../utils/period';

import { useDeleteConversationMutation, useGetConversationsQuery } from '@/apis/chatbotApis';
import { ChatConversation } from '@/apis/chatbotApis/types';
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

/** Conversations (newest first) under their period's heading, the order kept. */
const byPeriod = (conversations: ChatConversation[]) => {
  const groups: { period: string; items: ChatConversation[] }[] = [];
  for (const c of conversations) {
    const period = periodOf(c.updatedAt);
    const last = groups[groups.length - 1];
    if (last?.period === period) last.items.push(c);
    else groups.push({ period, items: [c] });
  }
  return groups;
};

/** The signed-in user's conversations, newest first under date headings — the history column. */
const ConversationList = ({
  activeConversationId,
  onSelect,
  onNewChat,
  busy = false,
  onDone,
}: ConversationListProps) => {
  const theme = useTheme();
  const { profile } = useAiChatConfig();
  // Only this assistant's: another one's conversation would be continued with this one's prompt.
  const { data, isFetching } = useGetConversationsQuery(profile, {
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
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
      <Box sx={{ p: 1.5, pb: 1 }}>
        <Button
          fullWidth
          variant="outlined"
          disabled={busy}
          startIcon={<EditNoteIcon />}
          onClick={() => {
            onNewChat();
            onDone?.();
          }}
          sx={{
            justifyContent: 'flex-start',
            borderRadius: 2.5,
            py: 0.9,
            color: 'text.primary',
            borderColor: hairline(theme),
            bgcolor: theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.03)' : 'rgba(0,0,0,0.02)',
            '&:hover': { borderColor: ACCENT, bgcolor: accentWash(theme, 0.08) },
          }}
        >
          新對話
        </Button>
      </Box>

      {isFetching && conversations.length === 0 ? (
        <CenteredSpinner size={24} sx={{ py: 4 }} />
      ) : conversations.length === 0 ? (
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ textAlign: 'center', py: 4, px: 2 }}
        >
          還沒有任何對話
        </Typography>
      ) : (
        <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 1, pb: 1.5 }}>
          {byPeriod(conversations).map((group) => (
            <Box key={group.period} component="section" sx={{ mt: 1 }}>
              <Typography
                variant="caption"
                component="h3"
                sx={{
                  display: 'block',
                  px: 1.25,
                  py: 0.75,
                  m: 0,
                  color: 'text.secondary',
                  fontWeight: 600,
                }}
              >
                {group.period}
              </Typography>
              <List disablePadding>
                {group.items.map((c) => {
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
                        position: 'relative',
                        borderRadius: 2,
                        mb: 0.25,
                        py: 0.75,
                        pl: 1.25,
                        pr: 0.5,
                        minHeight: 38,
                        // The delete button shows on hover, on keyboard focus, on the open
                        // conversation, and always where there is no hover (touch screens).
                        '& .conversation-delete': { opacity: 0, transition: 'opacity 120ms' },
                        '&:hover .conversation-delete, &:focus-within .conversation-delete, &.Mui-selected .conversation-delete':
                          { opacity: 1 },
                        '@media (hover: none)': { '& .conversation-delete': { opacity: 1 } },
                        '&.Mui-selected, &.Mui-selected:hover': {
                          bgcolor: accentWash(theme, 0.14),
                        },
                        '&.Mui-selected::before': {
                          content: '""',
                          position: 'absolute',
                          left: 0,
                          top: 8,
                          bottom: 8,
                          width: 3,
                          borderRadius: 3,
                          bgcolor: ACCENT,
                        },
                      }}
                    >
                      <Typography
                        variant="body2"
                        noWrap
                        title={c.title}
                        sx={{ flex: 1, minWidth: 0, fontWeight: active ? 600 : 400 }}
                      >
                        {c.title}
                      </Typography>
                      <IconButton
                        className="conversation-delete"
                        size="small"
                        disabled={isDeleting || busy}
                        onClick={(e) => handleDelete(e, c.id)}
                        aria-label="刪除對話"
                        sx={{ color: 'text.secondary', '&:hover': { color: 'error.main' } }}
                      >
                        <DeleteOutlineIcon sx={{ fontSize: 18 }} />
                      </IconButton>
                    </ListItemButton>
                  );
                })}
              </List>
            </Box>
          ))}
        </Box>
      )}
    </Box>
  );
};

export default ConversationList;
