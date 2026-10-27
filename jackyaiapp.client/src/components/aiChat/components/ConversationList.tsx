import EditNoteIcon from '@mui/icons-material/EditNote';
import SearchIcon from '@mui/icons-material/Search';
import { Box, Button, InputAdornment, List, ListItem, TextField, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';

import { useAiChatConfig } from '../config';
import { noSelectionAsk } from '../selection/readSelection';
import { ACCENT, accentWash, hairline } from '../style';
import {
  ConversationFind,
  findConversations,
  MAX_SEARCH_LENGTH,
  SEARCH_FROM,
  SEARCH_PAUSE_MS,
} from '../utils/conversationSearch';
import { RENAME_FAILED_TEXT } from '../utils/conversationTitle';
import { focusAfterDelete } from '../utils/focusAfterDelete';
import { periodOf } from '../utils/period';

import ConversationRow from './ConversationRow';
import ConversationTitleEditor from './ConversationTitleEditor';

import {
  useDeleteConversationMutation,
  useGetConversationsQuery,
  useRenameConversationMutation,
  useSearchConversationsQuery,
} from '@/apis/chatbotApis';
import { ChatConversation } from '@/apis/chatbotApis/types';
import CenteredSpinner from '@/components/CenteredSpinner';

interface ConversationListProps {
  activeConversationId: string;
  /** `messageId`: the message a search found it by, to be scrolled to (D-193). */
  onSelect: (conversationId: string, messageId?: number) => void;
  onNewChat: () => void;
  /** A reply is being written: switching away from it, or starting over, has to wait. */
  busy?: boolean;
  /** Called after a pick or a new chat, for a list shown in a drawer that should close then. */
  onDone?: () => void;
}

/** How long the column says a rename was refused. */
const RENAME_FAILED_MS = 5000;

type Find = ConversationFind<ChatConversation>;

/** Conversations (newest first) under their period's heading, the order kept. */
const byPeriod = (finds: Find[]) => {
  const groups: { period: string; items: Find[] }[] = [];
  for (const f of finds) {
    const period = periodOf(f.conversation.updatedAt);
    const last = groups[groups.length - 1];
    if (last?.period === period) last.items.push(f);
    else groups.push({ period, items: [f] });
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
  const [renameConversation] = useRenameConversationMutation();
  // The conversation whose title is a box in its row (D-144).
  const [editingId, setEditingId] = useState<string | null>(null);
  // A box closed from the keyboard gives the focus back to its row's pencil, which mounts again then (D-146). Only on
  // that commit: a row that does not come back (its new title is not what the search asks for, or it was deleted)
  // must not take the focus whenever it shows again later (D-148).
  const refocusPencil = useRef<string | null>(null);
  useLayoutEffect(() => {
    if (editingId === null) refocusPencil.current = null;
  }, [editingId]);
  // A rename the server refused: its title is back as it was, and the column says why for a moment (D-146).
  const [renameFailed, setRenameFailed] = useState(false);
  useEffect(() => {
    if (!renameFailed) return;
    const timer = setTimeout(() => setRenameFailed(false), RENAME_FAILED_MS);
    return () => clearTimeout(timer);
  }, [renameFailed]);

  const handleRename = async (id: string, title: string) => {
    setRenameFailed(false);
    const result = await renameConversation({ conversationId: id, title, profile });
    if ('error' in result) setRenameFailed(true);
  };

  const conversations = data?.data ?? [];
  // Finding one by its title (D-143), once there are enough to look for, and by what was said in it once the typing
  // pauses (D-191): the server's finds join the titles' when they are for the words in the box now.
  const [query, setQuery] = useState('');
  const searchable = conversations.length >= SEARCH_FROM;
  const typed = searchable ? query.trim() : '';
  const [paused, setPaused] = useState('');
  useEffect(() => {
    const timer = setTimeout(() => setPaused(typed), SEARCH_PAUSE_MS);
    return () => clearTimeout(timer);
  }, [typed]);
  const messageSearch = useSearchConversationsQuery(
    { profile, query: paused },
    { skip: paused === '' },
  );
  // Only what was asked for the words in the box now: an answer, or a failure, for the words before is not theirs.
  const current = paused === typed;
  const messageHits = current ? messageSearch.currentData?.data : undefined;
  const messagesFailed = current && messageSearch.isError;
  const lookingInMessages =
    typed !== '' && (!current || (messageSearch.isFetching && !messagesFailed));
  const shown: Find[] = searchable
    ? findConversations(conversations, query, messageHits)
    : conversations.map((conversation) => ({ conversation }));

  const handleEdit = (id: string) => {
    setRenameFailed(false);
    setEditingId(id);
  };

  // After a delete pressed from the keyboard, the row that took its place gets the focus, or 新對話 when none is
  // left (D-187), once the list is drawn without it. Not when the reader has moved on meanwhile.
  const rootRef = useRef<HTMLDivElement>(null);
  const newChatRef = useRef<HTMLButtonElement>(null);
  const refocusRow = useRef<string | null>(null);
  // The pressed delete is disabled while it runs, so the browser drops the focus to <body>, and in a drawer its focus
  // trap then puts it on the drawer's paper: an element holding the column counts as ours too.
  const focusIsOurs = () => {
    const at = document.activeElement;
    const column = rootRef.current;
    return !at || !column || at.contains(column) || column.contains(at);
  };

  const handleDelete = async (id: string, byKey: boolean) => {
    const next = focusAfterDelete(
      shown.map((f) => f.conversation.id),
      id,
    );
    const result = await deleteConversation(id);
    if (id === activeConversationId && !('error' in result)) onNewChat();
    if (!byKey || 'error' in result) return;
    if (next !== null) refocusRow.current = next;
    else if (focusIsOurs()) newChatRef.current?.focus();
  };

  return (
    // The list of conversations is not one of the answers: a title selected here is not asked about.
    <Box
      ref={rootRef}
      {...noSelectionAsk()}
      sx={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}
    >
      <Box sx={{ p: 1.5, pb: 1 }}>
        <Button
          ref={newChatRef}
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
        {searchable && (
          <TextField
            size="small"
            fullWidth
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              // Escape empties the search first; an empty one lets it through (the drawer closes).
              if (e.key === 'Escape' && query) {
                e.stopPropagation();
                setQuery('');
              }
            }}
            placeholder="搜尋對話"
            slotProps={{
              htmlInput: { 'aria-label': '搜尋對話', maxLength: MAX_SEARCH_LENGTH },
              input: {
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon sx={{ fontSize: 18, color: 'text.secondary' }} />
                  </InputAdornment>
                ),
              },
            }}
            sx={{
              mt: 1,
              '& .MuiOutlinedInput-root': { borderRadius: 2.5 },
              '& .MuiOutlinedInput-notchedOutline': { borderColor: hairline(theme) },
              '& .Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: ACCENT },
            }}
          />
        )}
        {/* Always there, so a screen reader announces the words when they come (D-148). */}
        <Typography
          role="status"
          variant="caption"
          color="error"
          sx={{ display: 'block', px: 0.5, ...(renameFailed && { mt: 0.75 }) }}
        >
          {renameFailed ? RENAME_FAILED_TEXT : ''}
        </Typography>
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
      ) : shown.length === 0 ? (
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ textAlign: 'center', py: 4, px: 2, overflowWrap: 'anywhere' }}
        >
          {lookingInMessages
            ? `沒有標題含「${typed}」的對話，正在找對話內容…`
            : messagesFailed
              ? `沒有標題含「${typed}」的對話（對話內容這次沒有搜尋到）`
              : `沒有標題或內容含「${typed}」的對話`}
        </Typography>
      ) : (
        <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 1, pb: 1.5 }}>
          {byPeriod(shown).map((group) => (
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
                {group.items.map(({ conversation: c, snippet, messageId }) => {
                  const active = c.id === activeConversationId;
                  if (c.id === editingId)
                    return (
                      <ListItem key={c.id} disablePadding sx={{ mb: 0.25, minHeight: 38, px: 0.5 }}>
                        <ConversationTitleEditor
                          title={c.title}
                          onSave={(title) => void handleRename(c.id, title)}
                          onClose={(byKey) => {
                            refocusPencil.current = byKey ? c.id : null;
                            setEditingId(null);
                          }}
                        />
                      </ListItem>
                    );
                  return (
                    <ConversationRow
                      key={c.id}
                      conversation={c}
                      snippet={snippet}
                      query={typed}
                      active={active}
                      busy={busy}
                      deleting={isDeleting}
                      onSelect={() => {
                        onSelect(c.id, messageId);
                        onDone?.();
                      }}
                      onEdit={() => handleEdit(c.id)}
                      onDelete={(byKey) => void handleDelete(c.id, byKey)}
                      selectRef={(el) => {
                        if (el && refocusRow.current === c.id) {
                          refocusRow.current = null;
                          if (focusIsOurs()) el.focus();
                        }
                      }}
                      pencilRef={(el) => {
                        if (el && refocusPencil.current === c.id) {
                          refocusPencil.current = null;
                          el.focus();
                        }
                      }}
                    />
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
