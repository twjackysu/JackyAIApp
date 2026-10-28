import { Box, ButtonBase, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';

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

/** A message a search found (D-193) is scrolled to this far below the top, and lit for this long. */
const REVEAL_MARGIN_PX = 16;
export const REVEAL_MS = 2400;

/** Scrolls `box` so that `target` sits just below its top. */
const scrollToTarget = (box: HTMLElement, target: HTMLElement) => {
  box.scrollTop +=
    target.getBoundingClientRect().top - box.getBoundingClientRect().top - REVEAL_MARGIN_PX;
};

/** A stored message to scroll to and light up once, as its conversation opens from a search's find (D-193). */
export interface MessageReveal {
  messageId: number;
}

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
  /** The last answer failed in a way asking again may fix: ask its question again in its place (D-139). */
  onRetry?: (answer: Message) => void;
  /** Scroll to this stored message (and light it up) once it is drawn, rather than staying at the end. */
  reveal?: MessageReveal | null;
  /** The conversation shown: opening another one goes to its end, wherever the last one was left. */
  conversationId?: string | null;
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
  onRetry,
  reveal,
  conversationId,
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
  const latest = useRef({ onPreview, onEdit, onDecide, onAlwaysAllow, onFollowUp, onRetry });
  useLayoutEffect(() => {
    latest.current = { onPreview, onEdit, onDecide, onAlwaysAllow, onFollowUp, onRetry };
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
      onRetry: (answer) => latest.current.onRetry?.(answer),
    }),
    [],
  );
  const scrollRef = useRef<HTMLDivElement>(null);
  const followingRef = useRef(true);
  const messageCountRef = useRef(messages.length);
  const conversationRef = useRef(conversationId);
  /** A found message the box keeps in place while what is drawn above it settles (D-197). */
  const anchorRef = useRef<{ target: HTMLElement; until: number } | null>(null);
  /**
   * Whether a column grown with no new message is followed too: not once the reader has pressed something in it, until
   * the next message or the end of the answer being written (D-197).
   */
  const settlingRef = useRef(true);

  // Scrolls this box itself, never the page: the chat now sits inside the page, and
  // scrollIntoView on a sentinel would drag the whole page along with every streamed token.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el || !active) return;
    // A new bubble (the reader's own message, or the reply starting) always follows to the end;
    // a bubble that is merely growing as it streams follows only if the reader has not scrolled up.
    // So does another conversation, even one as long as the last: where the last was left says nothing about it.
    if (messages.length !== messageCountRef.current || conversationId !== conversationRef.current) {
      messageCountRef.current = messages.length;
      conversationRef.current = conversationId;
      followingRef.current = true;
      anchorRef.current = null;
      settlingRef.current = true;
    }
    if (followingRef.current) el.scrollTop = el.scrollHeight;
  }, [messages, active, conversationId]);

  // A conversation opened from a search's find shows the message it was found in, not its end (D-193): once, after the
  // effect above has gone to the end. Each message is one element of the column, in order.
  const columnRef = useRef<HTMLDivElement>(null);
  const revealedRef = useRef<MessageReveal | null>(null);
  const lit = useRef<{ target: HTMLElement; timer: ReturnType<typeof setTimeout> } | null>(null);
  const unlight = useCallback(() => {
    if (!lit.current) return;
    clearTimeout(lit.current.timer);
    lit.current.target.removeAttribute('data-revealed');
    lit.current = null;
  }, []);
  useEffect(() => {
    const box = scrollRef.current;
    if (!reveal || !box || !active || revealedRef.current === reveal) return;
    const index = messages.findIndex((m) => m.serverId === reveal.messageId);
    const target = index < 0 ? null : columnRef.current?.children[index];
    if (!(target instanceof HTMLElement)) return;
    revealedRef.current = reveal;
    scrollToTarget(box, target);
    followingRef.current = false;
    anchorRef.current = { target, until: Date.now() + REVEAL_MS };
    // The same find opened again while still lit lights it again for the whole time: the old timer must not put
    // it out early, and an attribute that stays set would not restart the animation, so it is taken off and the
    // element laid out once before it goes back on.
    unlight();
    void target.offsetWidth;
    target.setAttribute('data-revealed', '');
    // Not cleared with the effect: a later render must not leave the message lit, or put it out early.
    lit.current = { target, timer: setTimeout(unlight, REVEAL_MS) };
  }, [reveal, messages, active, unlight]);
  useEffect(() => unlight, [unlight]);

  // An answer that ends draws its last parts (the questions to carry on with, a late card) after the reader may have
  // pressed something while it was written (its confirmation card's 允許): those still follow the end if the reader does.
  const wasBusyRef = useRef(busy);
  useEffect(() => {
    if (wasBusyRef.current && !busy) settlingRef.current = true;
    wasBusyRef.current = busy;
  }, [busy]);

  // Content drawn after the scroll was set — a card loaded lazily, a reply's late parts — makes the column taller with no
  // new message, so neither effect above runs again (D-197). While the reader follows the end it keeps them there; for a
  // while after a find was opened it keeps that message where it was put. Either only until the reader scrolls, presses
  // or types in the conversation: what grows then (a table they drew as a chart, a quiz they answered) is theirs to look
  // at where it is, not to be scrolled away from.
  useEffect(() => {
    const box = scrollRef.current;
    const column = columnRef.current;
    if (!active || !box || !column || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(() => {
      const anchor = anchorRef.current;
      if (anchor) {
        if (Date.now() < anchor.until && anchor.target.isConnected) {
          scrollToTarget(box, anchor.target);
          return;
        }
        anchorRef.current = null;
      }
      if (followingRef.current && settlingRef.current) box.scrollTop = box.scrollHeight;
    });
    observer.observe(column);
    const letGo = () => {
      anchorRef.current = null;
      settlingRef.current = false;
    };
    const events = ['wheel', 'touchstart', 'pointerdown', 'keydown'] as const;
    for (const name of events) box.addEventListener(name, letGo, { passive: true });
    return () => {
      observer.disconnect();
      for (const name of events) box.removeEventListener(name, letGo);
    };
  }, [active]);

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
        ref={columnRef}
        sx={{
          maxWidth: COLUMN_MAX_WIDTH,
          mx: 'auto',
          minHeight: '100%',
          display: 'flex',
          flexDirection: 'column',
          gap: 3,
          '@keyframes revealFlash': {
            from: {
              backgroundColor: accentWash(theme, 0.16),
              boxShadow: `0 0 0 8px ${accentWash(theme, 0.16)}`,
            },
            to: { backgroundColor: 'transparent', boxShadow: '0 0 0 8px transparent' },
          },
          '& > [data-revealed]': {
            animation: `revealFlash ${REVEAL_MS}ms ease-out`,
            borderRadius: 2,
          },
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
            canRetry={Boolean(onRetry) && message === last && !busy && message.retryable === true}
          />
        ))}
      </Box>
    </Box>
  );
}

export default MessageArea;
