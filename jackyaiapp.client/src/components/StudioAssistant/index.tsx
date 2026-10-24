import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import Box from '@mui/material/Box';
import Drawer from '@mui/material/Drawer';
import Fab from '@mui/material/Fab';
import { useTheme } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';

import { ASK_STUDIO_EVENT, askedOf, type AskStudioDetail } from './askStudio';
import QuickActions from './QuickActions';
import { quickActionsFor, type QuickAction } from './quickActions';
import { isStudioShortcut, studioChatConfig, studioShownOn } from './studioChat';

import type { AssistantActivity } from '@/components/aiChat';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useLazyGetDashboardsQuery } from '@/apis/dashboardApis';
import { AiChatConfigContext } from '@/components/aiChat/config';
import { ACCENT_GRADIENT } from '@/components/aiChat/style';
import CenteredSpinner from '@/components/CenteredSpinner';
import { ActionTooltip } from '@/components/hints';

const AiAssistantPanel = lazy(() => import('@/components/aiChat'));

/**
 * Studio AI on every page (docs/ai-native/design/05 §1): a button in the lower right corner, or ⌘K /
 * Ctrl+K, opens the assistant in a drawer on the right (the whole screen on a phone). Only for a
 * signed-in reader, and not on pages that have an assistant of their own. The conversation stays
 * while the drawer is closed, a reply still being written included. A page can also open it with a
 * message sent (`askStudio`, the home page's briefing).
 */
export default function StudioAssistant() {
  const { pathname } = useLocation();
  const { data: user } = useGetUserInfoQuery();
  const signedIn = !!user?.data;
  const shown = signedIn && studioShownOn(pathname);

  const [open, setOpen] = useState(false);
  // Mounted on first open and kept, so the conversation lives on.
  const [opened, setOpened] = useState(false);
  const [draft, setDraft] = useState('');
  // A message a page asked Studio AI (askStudio); the id tells the panel a new one from the last.
  // Kept until the panel has sent it, then dropped (the id keeps counting).
  const [ask, setAsk] = useState<(AskStudioDetail & { id: number }) | undefined>();
  const askId = useRef(0);
  const askSent = useCallback(() => setAsk(undefined), []);
  // The reader's dashboards (the /dashboards list), read each time the drawer opens (Studio AI may have saved a
  // new one meanwhile, on the server): the command bar opens them by name, and on /dashboards a suggestion reads one.
  const [loadBoards, { data: boardList }] = useLazyGetDashboardsQuery();
  useEffect(() => {
    if (open) void loadBoards();
  }, [open, loadBoards]);
  const boards = boardList?.data;
  const config = useMemo(() => studioChatConfig(pathname, boards), [pathname, boards]);

  // An answer that takes the reader to a page (open_page with go): the page changes under the drawer; on a
  // phone, where the drawer is the whole screen, it closes once the answer is done (they read what to do there
  // first), so they see where they are. A page with an assistant of its own (/finance) has no Studio AI to read
  // the answer in, so going there waits for the answer too. The conversation stays.
  const navigate = useNavigate();
  const fullScreen = useMediaQuery(useTheme().breakpoints.down('sm'));
  const afterAnswer = useRef<{ close: boolean; path?: string }>({ close: false });
  // The reader went somewhere with a quick action while an answer was written: the rest of that answer
  // takes them nowhere (cleared when it is done).
  const tookOver = useRef(false);
  const busyRef = useRef(false);
  const goTo = useCallback(
    (path: string) => {
      if (tookOver.current) return;
      if (studioShownOn(path)) navigate(path);
      else afterAnswer.current.path = path;
      if (fullScreen) afterAnswer.current.close = true;
    },
    [navigate, fullScreen],
  );
  const onActivity = useCallback(
    ({ busy }: AssistantActivity) => {
      busyRef.current = busy;
      if (busy) return;
      tookOver.current = false;
      const { close, path } = afterAnswer.current;
      afterAnswer.current = { close: false };
      if (close) setOpen(false);
      if (path) navigate(path);
    },
    [navigate],
  );
  // A quick action (what is typed names a page or one of the reader's dashboards, or is one English word)
  // goes there at once, without the AI: the box is emptied and the drawer closes, as a command bar would, on
  // every screen. The reader has taken over, so a page an answer still being written was going to take them
  // to is dropped.
  const quickActions = useMemo(
    () => quickActionsFor(draft, pathname, boards ?? []),
    [draft, pathname, boards],
  );
  const pickQuick = useCallback(
    (action: QuickAction) => {
      afterAnswer.current = { close: false };
      tookOver.current = busyRef.current;
      setDraft('');
      setOpen(false);
      navigate(action.path);
    },
    [navigate],
  );

  // Closed by hand meanwhile: the reader has taken over, nothing closes it for them later.
  const closeByHand = useCallback(() => {
    afterAnswer.current.close = false;
    setOpen(false);
  }, []);

  useEffect(() => {
    if (!shown) return;
    const onKey = (e: KeyboardEvent) => {
      if (!isStudioShortcut(e)) return;
      e.preventDefault();
      setOpened(true);
      setOpen((was) => !was);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [shown]);

  useEffect(() => {
    if (!shown) return;
    const onAsk = (e: Event) => {
      const asked = askedOf(e);
      if (!asked) return;
      setOpened(true);
      setOpen(true);
      askId.current += 1;
      setAsk({ id: askId.current, ...asked });
    };
    window.addEventListener(ASK_STUDIO_EVENT, onAsk);
    return () => window.removeEventListener(ASK_STUDIO_EVENT, onAsk);
  }, [shown]);

  // A page without the button closes the drawer (the conversation is kept for when it comes back).
  useEffect(() => {
    if (!shown) setOpen(false);
  }, [shown]);

  // Signed out, there is nothing to keep. On a page without the button (one of Studio's own links goes to
  // /finance) only the button goes: unmounting the panel would drop the conversation mid-reply.
  if (!signedIn || (!shown && !opened)) return null;

  return (
    <AiChatConfigContext value={config}>
      {shown && (
        <ActionTooltip title="Studio AI（⌘K）">
          <Fab
            aria-label="開啟 Studio AI"
            onClick={() => {
              setOpened(true);
              setOpen(true);
            }}
            sx={{
              position: 'fixed',
              right: { xs: 16, sm: 24 },
              bottom: { xs: 'calc(16px + env(safe-area-inset-bottom))', sm: 24 },
              zIndex: (theme) => theme.zIndex.speedDial,
              color: '#fff',
              backgroundImage: ACCENT_GRADIENT,
              display: open ? 'none' : undefined,
            }}
          >
            <AutoAwesomeIcon />
          </Fab>
        </ActionTooltip>
      )}
      <Drawer
        anchor="right"
        open={open}
        onClose={closeByHand}
        keepMounted={opened}
        slotProps={{ paper: { sx: { width: { xs: '100%', sm: 480 }, maxWidth: '100%' } } }}
      >
        {opened && (
          <Box sx={{ height: '100%' }}>
            <Suspense fallback={<CenteredSpinner />}>
              <AiAssistantPanel
                variant="sheet"
                active={open}
                inputValue={draft}
                ask={ask}
                onAskSent={askSent}
                onNavigate={goTo}
                onActivityChange={onActivity}
                onInputChange={setDraft}
                onCollapse={closeByHand}
                inputAccessory={<QuickActions actions={quickActions} onPick={pickQuick} />}
              />
            </Suspense>
          </Box>
        )}
      </Drawer>
    </AiChatConfigContext>
  );
}
