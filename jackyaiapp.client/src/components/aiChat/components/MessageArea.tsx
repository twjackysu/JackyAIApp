import { Box, ButtonBase, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';

import { useAiChatConfig } from '../config';
import { ACCENT, accentWash, hairline } from '../style';
import { Message } from '../types';
import { ApprovalCall, ApprovalDecision } from '../utils/approval';
import { followUpsFor } from '../utils/followUps';

import AssistantMark from './AssistantMark';
import MessageItem, { MessageHandlers } from './MessageItem';

import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

/** Within this many pixels of the end, the reader counts as "following" the conversation. */
const FOLLOW_THRESHOLD_PX = 80;

/** The conversation reads as one column of this width, however wide the panel. */
const COLUMN_MAX_WIDTH = 780;

interface MessageAreaProps {
  messages: Message[];
  onPreview: (block: ParsedBlock) => void;
  /** False while the panel's tab is hidden: a hidden element has no scroll position to keep. */
  active: boolean;
  /** A suggested question under the greeting was picked: put it in the message box. */
  onSuggest?: (question: string) => void;
  /** An answer is being written: no editing, and its own actions wait until it is done. */
  busy?: boolean;
  /** The reader edited one of their messages: send the new wording in its place. */
  onEdit?: (message: Message, text: string) => void;
  /** The reader allowed or refused the writes an answer is waiting on. */
  onDecide?: (pendingId: string, decisions: ApprovalDecision[]) => void;
  /** Lets the tools of these allowed calls run on their own from now on (the approval card's checkbox). */
  onAlwaysAllow?: (calls: ApprovalCall[]) => void;
  /** A question offered under the last answer was picked: send it. */
  onFollowUp?: (question: string) => void;
}

/** An empty conversation: what the assistant does, and a few questions to start from. */
const Welcome = ({ onSuggest }: { onSuggest?: (question: string) => void }) => {
  const theme = useTheme();
  const { greetingTitle, greeting, suggestions } = useAiChatConfig();
  return (
    <Box
      sx={{
        minHeight: '100%',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        textAlign: 'center',
        gap: 1.5,
        py: 3,
      }}
    >
      <AssistantMark size={48} />
      <Typography variant="h6" component="h2" sx={{ fontWeight: 700, mt: 1 }}>
        {greetingTitle}
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ maxWidth: 520, lineHeight: 1.7 }}>
        {greeting}
      </Typography>
      {onSuggest && (
        <Box
          data-testid="greeting-suggestions"
          sx={{
            mt: 1.5,
            width: '100%',
            maxWidth: 560,
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' },
            gap: 1,
          }}
        >
          {suggestions.map(({ text: q, icon: Icon }) => {
            return (
              <ButtonBase
                key={q}
                onClick={() => onSuggest(q)}
                sx={{
                  justifyContent: 'flex-start',
                  gap: 1.25,
                  px: 1.75,
                  py: 1.25,
                  borderRadius: 3,
                  textAlign: 'left',
                  border: `1px solid ${hairline(theme)}`,
                  bgcolor:
                    theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.025)' : 'rgba(0,0,0,0.015)',
                  transition: 'border-color 120ms, background-color 120ms',
                  '&:hover, &.Mui-focusVisible': {
                    borderColor: ACCENT,
                    bgcolor: accentWash(theme, 0.08),
                  },
                }}
              >
                <Icon sx={{ fontSize: 18, color: ACCENT }} />
                <Typography variant="body2">{q}</Typography>
              </ButtonBase>
            );
          })}
        </Box>
      )}
    </Box>
  );
};

function MessageArea({
  messages,
  onPreview,
  active,
  onSuggest,
  busy = false,
  onEdit,
  onDecide,
  onAlwaysAllow,
  onFollowUp,
}: MessageAreaProps) {
  const theme = useTheme();
  const { followUps: followUpRules } = useAiChatConfig();
  // Only under the last answer, once nothing is being written.
  const last = messages[messages.length - 1];
  const previousContent = messages[messages.length - 2]?.content;
  // Kept while the last answer and its question stay the same, so typing in the box draws no message again.
  const followUps = useMemo(
    () => (last && !busy ? followUpsFor(last, previousContent, followUpRules) : []),
    [last, previousContent, busy, followUpRules],
  );
  // The reader's message being edited in place, by its id.
  const [editingId, setEditingId] = useState<string | null>(null);

  // The page's callbacks are new functions on most renders (inline arrows); the messages get one object that
  // calls the latest of them, so a message that did not change keeps its props and is not drawn again.
  const latest = useRef({ onPreview, onEdit, onDecide, onAlwaysAllow, onFollowUp });
  useLayoutEffect(() => {
    latest.current = { onPreview, onEdit, onDecide, onAlwaysAllow, onFollowUp };
  });
  const handlers = useMemo<MessageHandlers>(
    () => ({
      onPreview: (block) => latest.current.onPreview(block),
      onStartEdit: (id) => setEditingId(id),
      onCancelEdit: () => setEditingId(null),
      onSubmitEdit: (message, text) => {
        setEditingId(null);
        latest.current.onEdit?.(message, text);
      },
      onDecide: (pendingId, decisions) => latest.current.onDecide?.(pendingId, decisions),
      onAlwaysAllow: (calls) => latest.current.onAlwaysAllow?.(calls),
      onFollowUp: (question) => latest.current.onFollowUp?.(question),
    }),
    [],
  );
  const scrollRef = useRef<HTMLDivElement>(null);
  const followingRef = useRef(true);
  const messageCountRef = useRef(messages.length);

  // Scrolls this box itself, never the page: the chat now sits inside the page, and
  // scrollIntoView on a sentinel would drag the whole page along with every streamed token.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el || !active) return;
    // A new bubble (the reader's own message, or the reply starting) always follows to the end;
    // a bubble that is merely growing as it streams follows only if the reader has not scrolled up.
    if (messages.length !== messageCountRef.current) {
      messageCountRef.current = messages.length;
      followingRef.current = true;
    }
    if (followingRef.current) el.scrollTop = el.scrollHeight;
  }, [messages, active]);

  const handleScroll = () => {
    const el = scrollRef.current;
    if (!el || !active) return;
    followingRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < FOLLOW_THRESHOLD_PX;
  };

  return (
    <Box
      ref={scrollRef}
      onScroll={handleScroll}
      sx={{
        flex: 1,
        minHeight: 0,
        overflowY: 'auto',
        px: { xs: 1.5, sm: 3 },
        py: 3,
        '&::-webkit-scrollbar': { width: 8 },
        '&::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
        '&::-webkit-scrollbar-thumb': { backgroundColor: hairline(theme), borderRadius: 4 },
      }}
    >
      <Box
        sx={{
          maxWidth: COLUMN_MAX_WIDTH,
          mx: 'auto',
          minHeight: '100%',
          display: 'flex',
          flexDirection: 'column',
          gap: 3,
        }}
      >
        {messages.length === 0 && <Welcome onSuggest={onSuggest} />}

        {messages.map((message, index) => (
          <MessageItem
            key={message.id}
            message={message}
            handlers={handlers}
            busy={busy}
            answering={busy && index === messages.length - 1}
            editing={editingId === message.id}
            canEditMessages={Boolean(onEdit)}
            canAlwaysAllow={Boolean(onAlwaysAllow)}
            active={active}
            followUps={message === last && onFollowUp ? followUps : undefined}
          />
        ))}
      </Box>
    </Box>
  );
}

export default MessageArea;
