import Box from '@mui/material/Box';
import Drawer from '@mui/material/Drawer';
import { useTheme } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';

import { ASK_STUDIO_EVENT, askedOf } from './askStudio';
import DrawerResizeHandle from './DrawerResizeHandle';
import { useDrawerWidth } from './drawerWidth';
import { quickActionsFor, type QuickAction } from './quickActions';
import { refreshAfterTool } from './refreshAfterTool';
import { isSlashDraft, slashCommandsFor, type SlashCommand } from './slashCommands';
import { isStudioShortcut, studioChatConfig, studioShownOn } from './studioChat';
import StudioFab from './StudioFab';
import StudioGuest from './StudioGuest';

import type { AssistantActivity } from '@/components/aiChat';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useLazyGetDashboardsQuery } from '@/apis/dashboardApis';
import { draftWithAppText } from '@/components/aiChat/cards/mcpApp/appDraft';
import QuickChips from '@/components/aiChat/components/QuickChips';
import { AiChatConfigContext } from '@/components/aiChat/config';
import { useAskQueue } from '@/components/aiChat/hooks/useAskQueue';
import {
  draftWithSelection,
  selectionStepOf,
  type SelectionActionKind,
} from '@/components/aiChat/selection/selectionActions';
import SelectionBar from '@/components/aiChat/selection/SelectionBar';
import { handoffState } from '@/components/aiChat/utils/handoff';
import CenteredSpinner from '@/components/CenteredSpinner';
import { registrySearchOf } from '@/Connectors/AddMcpServerDialog/registryView';
import { ACCOUNTS_ANCHOR, CONNECTOR_PARAM } from '@/Connectors/connectorView';
import { useAppDispatch } from '@/redux/hooks';

const AiAssistantPanel = lazy(() => import('@/components/aiChat'));

/** A chip over the message box: a quick action (no AI) or a ready-made request (`/`, sent to the AI). */
type QuickChip = QuickAction | SlashCommand;

/**
 * Studio AI on every page (docs/ai-native/design/05 §1): a button in the lower right corner, or ⌘K /
 * Ctrl+K, opens the assistant in a drawer on the right (the whole screen on a phone); a visitor not signed in gets the
 * same button, which says what it does and offers to sign in (`StudioGuest`). Not on pages that have an assistant of
 * their own. The conversation stays
 * while the drawer is closed, a reply still being written included. A page can also open it with a
 * message sent (`askStudio`, the home page's briefing).
 */
export default function StudioAssistant() {
  const { pathname, search, hash } = useLocation();
  const { data: user, isLoading: userLoading } = useGetUserInfoQuery();
  const signedIn = !!user?.data;
  const shown = signedIn && studioShownOn(pathname);

  const [open, setOpen] = useState(false);
  // Mounted on first open and kept, so the conversation lives on.
  const [opened, setOpened] = useState(false);
  const [draft, setDraft] = useState('');
  // The box holds text selected on the page (問 AI on a selection), until it is emptied (sent, or cleared):
  // what is sent from it is page text, possibly someone else's words (D-086, D-087).
  const [draftQuotesPage, setDraftQuotesPage] = useState(false);
  const changeDraft = useCallback((value: string) => {
    setDraft(value);
    if (!value.trim()) setDraftQuotesPage(false);
  }, []);
  // An MCP App's words the reader let into the box (ui/message, D-204): not the reader's, so they go as quoted text.
  const putAppText = useCallback((text: string) => {
    setDraft((typed) => draftWithAppText(typed, text));
    setDraftQuotesPage(true);
  }, []);
  // The messages pages asked Studio AI (askStudio, a selection), in order, each kept until the panel has sent it:
  // two asked before the panel has loaded both go.
  const { ask, enqueue: enqueueAsk, sent: askSent } = useAskQueue();
  // The reader's dashboards (the /dashboards list), read each time the drawer opens (Studio AI may have saved a
  // new one meanwhile, on the server): the command bar opens them by name, and on /dashboards a suggestion reads one.
  const [loadBoards, { data: boardList }] = useLazyGetDashboardsQuery();
  useEffect(() => {
    if (open) void loadBoards();
  }, [open, loadBoards]);
  const boards = boardList?.data;
  const config = useMemo(() => studioChatConfig(pathname, boards ?? []), [pathname, boards]);
  // A write Studio AI made (a holding updated, a word added) shows at once on the page under the drawer.
  const dispatch = useAppDispatch();
  const toolDone = useCallback(
    (tool: string) => refreshAfterTool(tool).forEach((action) => dispatch(action)),
    [dispatch],
  );

  // An answer that takes the reader to a page (open_page with go): the page changes under the drawer; on a
  // phone, where the drawer is the whole screen, it closes once the answer is done (they read what to do there
  // first), so they see where they are. A page with an assistant of its own (/finance) has no Studio AI to read
  // the answer in, so going there waits for the answer too. The conversation stays. A task handed to that page's own
  // assistant (open_page with `ask`, D-095) goes with it, once the answer is done as an answer: that page then asks
  // it, and the drawer, which that page does not show, closes.
  const navigate = useNavigate();
  const fullScreen = useMediaQuery(useTheme().breakpoints.down('sm'));
  // On a wide screen the drawer's left edge drags it wider (design/05 §1), remembered in this browser.
  const [drawerWidth, setDrawerWidth] = useDrawerWidth();
  const afterAnswer = useRef<{ close: boolean; path?: string; handoff?: string }>({ close: false });
  // The reader went somewhere with a quick action while an answer was written: the rest of that answer
  // takes them nowhere (cleared when it is done).
  const tookOver = useRef(false);
  const busyRef = useRef(false);
  const goTo = useCallback(
    (path: string, handoff?: string) => {
      if (tookOver.current) return;
      // The last page asked for wins: going somewhere now drops a page (and task) held for the end of the answer.
      if (studioShownOn(path)) {
        afterAnswer.current = { close: afterAnswer.current.close };
        navigate(path);
      } else afterAnswer.current = { ...afterAnswer.current, path, handoff };
      if (fullScreen) afterAnswer.current.close = true;
    },
    [navigate, fullScreen],
  );
  const onActivity = useCallback(
    ({ busy, answered }: AssistantActivity) => {
      busyRef.current = busy;
      if (busy) return;
      tookOver.current = false;
      const { close, path, handoff } = afterAnswer.current;
      afterAnswer.current = { close: false };
      if (close) setOpen(false);
      // Stopped or failed: the reader goes there, but nothing is asked in their name.
      if (path) navigate(path, handoff && answered ? { state: handoffState(handoff) } : undefined);
    },
    [navigate],
  );
  // A quick action (what is typed names a page or one of the reader's dashboards, or is one English word)
  // goes there at once, without the AI: the box is emptied and the drawer closes, as a command bar would, on
  // every screen. The reader has taken over, so a page an answer still being written was going to take them
  // to is dropped.
  // A draft starting with `/` lists the ready-made requests instead (`slashCommandsFor`, D-111): a press sends
  // one as if typed, in the drawer that stays open; the briefing in a new conversation, as the home page sends it.
  // The page's own come first (explaining the word on its dictionary page, reading the reader's dashboard; D-113).
  const quickActions = useMemo<QuickChip[]>(
    () =>
      isSlashDraft(draft)
        ? slashCommandsFor(draft, pathname, boards ?? [])
        : quickActionsFor(draft, pathname, boards ?? []),
    [draft, pathname, boards],
  );
  const pickQuick = useCallback(
    (action: QuickChip) => {
      if (action.kind === 'command') {
        changeDraft('');
        enqueueAsk({ text: action.ask, ...(action.fresh && { fresh: true }) });
        return;
      }
      afterAnswer.current = { close: false };
      tookOver.current = busyRef.current;
      changeDraft('');
      setOpen(false);
      navigate(action.path);
    },
    [navigate, changeDraft, enqueueAsk],
  );
  // Enter on a `/` draft sends the first ready-made request listed, as a press would (D-115); a quick action is only
  // ever pressed (Enter on a page's name asks the AI, as typed).
  const sendDraft = useCallback(
    (text: string) => {
      if (!isSlashDraft(text)) return false;
      const first = slashCommandsFor(text, pathname, boards ?? [])[0];
      if (!first) return false;
      pickQuick(first);
      return true;
    },
    [pathname, boards, pickQuick],
  );

  // Closed by hand meanwhile: the reader has taken over, nothing closes it for them later.
  const closeByHand = useCallback(() => {
    afterAnswer.current.close = false;
    setOpen(false);
  }, []);

  // What the reader selected (SelectionBar, `selectionStepOf`). On the page, explaining, translating or saving it is
  // sent at once in a conversation of its own (D-086); in one of Studio AI's answers (a word of the passage it wrote),
  // in the conversation on screen. Asking about it puts it in the box for the reader to finish. One English word's
  // dictionary page is a quick action, no AI; from the drawer the page changes under it, which on a phone (where the
  // drawer is the whole screen) then closes.
  const onSelection = useCallback(
    (kind: SelectionActionKind, text: string, inAssistant: boolean) => {
      const step = selectionStepOf(kind, text, inAssistant);
      if (!step) return;
      if (step.do === 'go') {
        // The reader went somewhere themselves, as a quick action does: an answer still being written no longer moves
        // the page (nor hands a task to the page it would have gone to) when it ends.
        afterAnswer.current = { close: false };
        tookOver.current = busyRef.current;
        navigate(step.path);
        if (inAssistant && fullScreen) setOpen(false);
        return;
      }
      setOpened(true);
      setOpen(true);
      if (step.do === 'draft') {
        setDraft((typed) => draftWithSelection(typed, step.text));
        if (step.quotesPageText) setDraftQuotesPage(true);
        return;
      }
      enqueueAsk({
        text: step.text,
        // Page text may be someone else's words: the server counts it as outside text (writes ask first).
        ...(step.fromPage && { fresh: true, quotesPageText: true }),
      });
    },
    [navigate, fullScreen, enqueueAsk],
  );

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
      enqueueAsk(asked);
    };
    window.addEventListener(ASK_STUDIO_EVENT, onAsk);
    return () => window.removeEventListener(ASK_STUDIO_EVENT, onAsk);
  }, [shown, enqueueAsk]);

  // A page without the button closes the drawer (the conversation is kept for when it comes back).
  useEffect(() => {
    if (!shown) setOpen(false);
  }, [shown]);

  // Sent to /connectors to add a server Studio AI found (the `mcp-servers` card's 加入, D-124), to reconnect one a
  // call was refused for (the failed call's card, D-223) or to connect an account (a native connector's card, D-225):
  // that is done on the page, so the drawer steps aside (on a phone it is the whole screen) and the conversation waits.
  const handledOnConnectorsPage =
    pathname === '/connectors' &&
    (registrySearchOf(new URLSearchParams(search)) !== null ||
      new URLSearchParams(search).has(CONNECTOR_PARAM) ||
      hash === `#${ACCOUNTS_ANCHOR}`);
  useEffect(() => {
    if (handledOnConnectorsPage) setOpen(false);
  }, [handledOnConnectorsPage]);

  // Signed out, the button says what Studio AI does and how to sign in (nothing is kept, nothing asked of the AI);
  // nothing while it is not known yet, so a signed-in reader's button never starts as the visitor's.
  if (!signedIn) return !userLoading && studioShownOn(pathname) ? <StudioGuest /> : null;
  // On a page without the button (one of Studio's own links goes to
  // /finance) only the button goes: unmounting the panel would drop the conversation mid-reply.
  if (!shown && !opened) return null;
  const selectionBar = <SelectionBar enabled={shown} onAction={onSelection} />;

  return (
    <AiChatConfigContext value={config}>
      {/* With the drawer open, the bar is inside it: outside, the drawer's focus trap would take back the focus that
          Alt+A moves into the bar (D-188). Page text cannot be selected behind the open drawer anyway. */}
      {!open && selectionBar}
      {shown && (
        <StudioFab
          onClick={() => {
            setOpened(true);
            setOpen(true);
          }}
          hidden={open}
        />
      )}
      <Drawer
        anchor="right"
        open={open}
        onClose={closeByHand}
        keepMounted={opened}
        slotProps={{ paper: { sx: { width: { xs: '100%', sm: drawerWidth }, maxWidth: '100%' } } }}
      >
        {!fullScreen && <DrawerResizeHandle width={drawerWidth} onChange={setDrawerWidth} />}
        {open && selectionBar}
        {opened && (
          <Box data-ai-assistant sx={{ height: '100%' }}>
            <Suspense fallback={<CenteredSpinner />}>
              <AiAssistantPanel
                variant="sheet"
                active={open}
                inputValue={draft}
                ask={ask}
                onAskSent={askSent}
                onNavigate={goTo}
                onToolDone={toolDone}
                onActivityChange={onActivity}
                onInputChange={changeDraft}
                inputQuotesPageText={draftQuotesPage}
                onCollapse={closeByHand}
                inputAccessory={
                  <QuickChips actions={quickActions} onPick={pickQuick} enterPicksFirst />
                }
                onSendDraft={sendDraft}
                onAppText={putAppText}
              />
            </Suspense>
          </Box>
        )}
      </Drawer>
    </AiChatConfigContext>
  );
}
