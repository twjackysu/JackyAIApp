import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import Box from '@mui/material/Box';
import Drawer from '@mui/material/Drawer';
import Fab from '@mui/material/Fab';
import { lazy, Suspense, useEffect, useMemo, useState } from 'react';
import { useLocation } from 'react-router-dom';

import { isStudioShortcut, studioChatConfig, studioShownOn } from './studioChat';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { AiChatConfigContext } from '@/components/aiChat/config';
import { ACCENT_GRADIENT } from '@/components/aiChat/style';
import CenteredSpinner from '@/components/CenteredSpinner';
import { ActionTooltip } from '@/components/hints';

const AiAssistantPanel = lazy(() => import('@/components/aiChat'));

/**
 * Studio AI on every page (docs/ai-native/design/05 §1): a button in the lower right corner, or ⌘K /
 * Ctrl+K, opens the assistant in a drawer on the right (the whole screen on a phone). Only for a
 * signed-in reader, and not on pages that have an assistant of their own. The conversation stays
 * while the drawer is closed, a reply still being written included.
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
  const config = useMemo(() => studioChatConfig(pathname), [pathname]);

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
        onClose={() => setOpen(false)}
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
                onInputChange={setDraft}
                onCollapse={() => setOpen(false)}
              />
            </Suspense>
          </Box>
        )}
      </Drawer>
    </AiChatConfigContext>
  );
}
