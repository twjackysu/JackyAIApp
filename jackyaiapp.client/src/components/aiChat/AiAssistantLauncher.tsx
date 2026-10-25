import Box from '@mui/material/Box';
import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';

import { CompactChatBar } from './components/CompactChatBar';
import { AiChatConfig, AiChatConfigContext } from './config';
import { useAskQueue } from './hooks/useAskQueue';
import {
  draftWithSelection,
  selectionStepOf,
  type SelectionActionKind,
} from './selection/selectionActions';
import SelectionBar from './selection/SelectionBar';
import { handoffFromState } from './utils/handoff';

import type { AssistantActivity } from '.';

// The panel (streaming, markdown, HTML preview, history) is a separate chunk. It is fetched once
// the page knows the visitor can use it, so opening the box does not wait on the network.
const loadPanel = () => import('.');
const AiAssistantPanel = lazy(loadPanel);

const IDLE: AssistantActivity = { busy: false, hasConversation: false };

interface AiAssistantLauncherProps {
  /** What this app's assistant is about (its greeting, examples, server profile). */
  config: AiChatConfig;
  isAuthenticated: boolean;
  /** The visitor's sign-in state is still being fetched. */
  isAuthLoading: boolean;
  /** An answer takes the reader to a page of the site (a tool result's `navigate`, e.g. Data Analysis's
   *  `save_to_dashboard` asked to open what it saved). Called once the answer is done: the assistant is part of
   *  this page, so leaving it while the answer is written would end it; not when the reader stopped it, or it failed.
   *  Without it, only the answer's link. */
  onNavigate?: (path: string) => void;
}

/**
 * An app's AI assistant on its page (Finance's first): a one-line box, no bigger than a search box,
 * until the visitor puts the cursor in it — then the whole conversation (history column, messages,
 * input) opens in its place. Folding it back keeps the conversation, and a reply still being
 * written carries on. Selecting text on the page (an analysis, a digest) or in its answers asks this assistant
 * about it, as Studio AI is asked elsewhere (`SelectionBar`, D-089); it has no tool to add a word to the
 * repository, so a word is explained instead.
 */
export function AiAssistantLauncher({
  config,
  isAuthenticated,
  isAuthLoading,
  onNavigate,
}: AiAssistantLauncherProps) {
  const [expanded, setExpanded] = useState(false);
  // Mounted on first open and kept: the conversation lives in the panel.
  const [opened, setOpened] = useState(false);
  const [draft, setDraft] = useState('');
  // The box holds text selected on the page (問 AI on a selection) until it is emptied: what is sent from it is
  // page text, possibly someone else's words (D-087).
  const [draftQuotesPage, setDraftQuotesPage] = useState(false);
  const changeDraft = useCallback((value: string) => {
    setDraft(value);
    if (!value.trim()) setDraftQuotesPage(false);
  }, []);
  // A selection's messages to send (explain, translate), each kept until the panel has sent it.
  const { ask, enqueue: enqueueAsk, sent: askSent } = useAskQueue();
  const [activity, setActivity] = useState<AssistantActivity>(IDLE);

  useEffect(() => {
    if (isAuthenticated) void loadPanel();
  }, [isAuthenticated]);

  const open = useCallback(() => {
    if (!isAuthenticated) return;
    setOpened(true);
    setExpanded(true);
  }, [isAuthenticated]);
  const collapse = useCallback(() => setExpanded(false), []);

  // What the reader selected (`selectionStepOf`): a word's dictionary page is a page to go to (leaving this one,
  // as any link would); the rest opens the conversation, from the page as a new one with `quotesPageText`.
  const navigate = useNavigate();
  const busy = useRef(false);
  const dictionaryAfterAnswer = useRef<string | undefined>(undefined);
  const onSelection = useCallback(
    (kind: SelectionActionKind, text: string, inAssistant: boolean) => {
      const step = selectionStepOf(kind, text, inAssistant);
      if (!step || !isAuthenticated) return;
      if (step.do === 'go') {
        // Leaving the page ends an answer being written: the dictionary waits until it is done.
        if (busy.current) dictionaryAfterAnswer.current = step.path;
        else navigate(step.path);
        return;
      }
      setOpened(true);
      setExpanded(true);
      if (step.do === 'draft') {
        setDraft((typed) => draftWithSelection(typed, step.text));
        if (step.quotesPageText) setDraftQuotesPage(true);
        return;
      }
      enqueueAsk({
        text: step.text,
        ...(step.fromPage && { fresh: true, quotesPageText: true }),
      });
    },
    [isAuthenticated, navigate, enqueueAsk],
  );

  // A task Studio AI handed to this page's assistant (D-095, `utils/handoff`): asked once, as the first message of a new
  // conversation, then taken out of the history entry, so going back or reloading does not ask it again. Only this
  // site's code sets the location state; the server sends it only from a conversation that read no outside text.
  const location = useLocation();
  const handedOver = useRef<string | null>(null);
  useEffect(() => {
    const text = handoffFromState(location.state);
    if (!text || !isAuthenticated || handedOver.current === location.key) return;
    handedOver.current = location.key;
    setOpened(true);
    setExpanded(true);
    enqueueAsk({ text, fresh: true });
    navigate(
      { pathname: location.pathname, search: location.search, hash: location.hash },
      { replace: true, state: null },
    );
  }, [location, isAuthenticated, enqueueAsk, navigate]);

  // Where an answer asked to go, kept until that answer is done (the last one asked wins).
  const goAfterAnswer = useRef<string | undefined>(undefined);
  const holdNavigation = useCallback((path: string) => {
    goAfterAnswer.current = path;
  }, []);
  const onActivity = useCallback(
    (next: AssistantActivity) => {
      setActivity(next);
      busy.current = next.busy;
      // A word's dictionary page the reader asked for while the answer was written: however the answer ended.
      const asked = dictionaryAfterAnswer.current;
      if (!next.busy && asked) {
        dictionaryAfterAnswer.current = undefined;
        goAfterAnswer.current = undefined;
        navigate(asked);
        return;
      }
      const path = goAfterAnswer.current;
      if (next.busy || !path) return;
      goAfterAnswer.current = undefined;
      // Stopped, or failed after the tool ran: the reader stays, with the answer's link.
      if (next.answered) onNavigate?.(path);
    },
    [onNavigate, navigate],
  );

  const bar = (
    <CompactChatBar
      value={draft}
      onChange={changeDraft}
      onActivate={open}
      state={isAuthLoading ? 'loading' : isAuthenticated ? 'ready' : 'signedOut'}
      busy={activity.busy}
      hasConversation={activity.hasConversation}
    />
  );

  return (
    <AiChatConfigContext value={config}>
      <SelectionBar
        enabled={isAuthenticated}
        assistantName={config.title ?? 'AI 助手'}
        canSaveWord={false}
        onAction={onSelection}
      />
      {/* More room below than between the market sections, which are one thing among themselves. */}
      <Box sx={{ mb: { xs: 3, sm: 4 } }}>
        {!expanded && bar}
        {opened && (
          // Its answers are the assistant's own: a selection there continues the conversation.
          <Box data-ai-assistant sx={{ display: expanded ? 'block' : 'none' }}>
            {/* Until the chunk arrives (normally already fetched), the box stays where it was. */}
            <Suspense fallback={bar}>
              <AiAssistantPanel
                active={expanded}
                inputValue={draft}
                onInputChange={changeDraft}
                inputQuotesPageText={draftQuotesPage}
                ask={ask}
                onAskSent={askSent}
                onCollapse={collapse}
                onActivityChange={onActivity}
                onNavigate={onNavigate ? holdNavigation : undefined}
              />
            </Suspense>
          </Box>
        )}
      </Box>
    </AiChatConfigContext>
  );
}
