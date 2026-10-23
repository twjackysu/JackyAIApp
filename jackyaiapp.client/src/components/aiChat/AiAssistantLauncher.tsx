import Box from '@mui/material/Box';
import { lazy, Suspense, useCallback, useEffect, useState } from 'react';

import { CompactChatBar } from './components/CompactChatBar';
import { AiChatConfig, AiChatConfigContext } from './config';

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
}

/**
 * An app's AI assistant on its page (Finance's first): a one-line box, no bigger than a search box,
 * until the visitor puts the cursor in it — then the whole conversation (history column, messages,
 * input) opens in its place. Folding it back keeps the conversation, and a reply still being
 * written carries on.
 */
export function AiAssistantLauncher({
  config,
  isAuthenticated,
  isAuthLoading,
}: AiAssistantLauncherProps) {
  const [expanded, setExpanded] = useState(false);
  // Mounted on first open and kept: the conversation lives in the panel.
  const [opened, setOpened] = useState(false);
  const [draft, setDraft] = useState('');
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

  const bar = (
    <CompactChatBar
      value={draft}
      onChange={setDraft}
      onActivate={open}
      state={isAuthLoading ? 'loading' : isAuthenticated ? 'ready' : 'signedOut'}
      busy={activity.busy}
      hasConversation={activity.hasConversation}
    />
  );

  return (
    <AiChatConfigContext value={config}>
      {/* More room below than between the market sections, which are one thing among themselves. */}
      <Box sx={{ mb: { xs: 3, sm: 4 } }}>
        {!expanded && bar}
        {opened && (
          <Box sx={{ display: expanded ? 'block' : 'none' }}>
            {/* Until the chunk arrives (normally already fetched), the box stays where it was. */}
            <Suspense fallback={bar}>
              <AiAssistantPanel
                active={expanded}
                inputValue={draft}
                onInputChange={setDraft}
                onCollapse={collapse}
                onActivityChange={setActivity}
              />
            </Suspense>
          </Box>
        )}
      </Box>
    </AiChatConfigContext>
  );
}
