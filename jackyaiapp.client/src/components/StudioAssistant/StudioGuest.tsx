import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';
import { useLocation } from 'react-router-dom';

import { isStudioShortcut } from './studioChat';
import StudioFab from './StudioFab';

/** What Studio AI does, as a visitor reads it before signing in. */
const GUEST_STUDIO_POINTS = [
  '首頁一鍵「AI 今日簡報」：今天的行程、該複習的單字、資產概況一次整理好',
  '用說的就能做事：查單字並加入單字庫、出複習測驗、記一筆交易、做 dashboard',
  '接上你自己的 MCP server 與 Google、Microsoft、Jira 帳號，AI 就能用它們',
  '會改變東西的動作，一定先問過你',
];

/** Where signing in from `path` (with its query) comes back to. */
function guestSignInUrl(pathWithQuery: string): string {
  return `/api/account/login/Google?ReturnUrl=${encodeURIComponent(pathWithQuery)}`;
}

/**
 * Studio AI for a visitor who is not signed in (design/05 §1): the same button and ⌘K / Ctrl+K, which open what it does
 * and a sign-in that comes back to this page, rather than nothing. Nothing is asked of the AI: it reads the reader's own
 * words, calendar and portfolio, so it needs who they are.
 */
export default function StudioGuest() {
  const { pathname, search } = useLocation();
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!isStudioShortcut(e)) return;
      e.preventDefault();
      setOpen((was) => !was);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  return (
    <>
      <StudioFab onClick={() => setOpen(true)} hidden={open} />
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        aria-labelledby="studio-guest-title"
        maxWidth="xs"
        fullWidth
      >
        <DialogTitle id="studio-guest-title">登入後可使用 Studio AI</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            每一頁都叫得出來的 AI 助理，會用本站自己的功能：
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {GUEST_STUDIO_POINTS.map((point) => (
              <Typography key={point} component="li" variant="body2" sx={{ mb: 0.75 }}>
                {point}
              </Typography>
            ))}
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
            免費試用。登入是為了讓它讀得到你自己的單字、行程與投資組合。
          </Typography>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setOpen(false)}>之後再說</Button>
          <Button variant="contained" href={guestSignInUrl(pathname + search)}>
            使用 Google 登入
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
