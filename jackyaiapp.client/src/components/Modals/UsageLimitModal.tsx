import VolunteerActivismIcon from '@mui/icons-material/VolunteerActivism';
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Link,
  Stack,
  Typography,
} from '@mui/material';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { AUTHOR_EMAIL, SPONSOR_URL } from '@/constants/author';
import { useAppDispatch, useAppSelector } from '@/redux/hooks';
import { closeUsageLimitModal } from '@/redux/uiSlice';

/**
 * Shown when an AI feature answers 429 + UsageLimitExceeded. The daily budget is a free trial
 * allowance the author pays for, so this asks for sponsorship and tells the user how to get the
 * limit reset or raised (the author does that by hand, per account).
 */
export default function UsageLimitModal() {
  const dispatch = useAppDispatch();
  const isUsageLimitModalOpen = useAppSelector((state) => state.ui.isUsageLimitModalOpen);
  // Only a signed-in user can hit the limit, so this is normally already cached.
  const { data: userInfo } = useGetUserInfoQuery(undefined, { skip: !isUsageLimitModalOpen });
  const accountEmail = userInfo?.data?.email;

  const handleClose = () => {
    dispatch(closeUsageLimitModal());
  };

  return (
    <Dialog
      open={isUsageLimitModalOpen}
      onClose={handleClose}
      maxWidth="xs"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            borderRadius: 3,
            p: 1,
          },
        },
      }}
    >
      <DialogTitle sx={{ textAlign: 'center', pb: 1 }}>
        <VolunteerActivismIcon color="warning" sx={{ fontSize: 48, mb: 1 }} />
        <Typography variant="h5" component="div" sx={{ fontWeight: 'bold' }}>
          今天的免費額度用完了
        </Typography>
      </DialogTitle>
      <DialogContent sx={{ pb: 2 }}>
        <Stack spacing={1.5}>
          <Typography variant="body1" color="text.secondary">
            這裡的 AI 功能是作者自掏腰包提供的，每個人每天有一份免費試用額度，台灣時間每天早上 8
            點重置。
          </Typography>
          <Typography variant="body1" color="text.secondary">
            如果你用得順手、想用更多，歡迎贊助作者分擔 AI 費用；也可以來信{' '}
            <Link href={`mailto:${AUTHOR_EMAIL}`}>{AUTHOR_EMAIL}</Link>
            ，作者可以幫你重置或調高額度。
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {accountEmail
              ? `來信時請附上你登入用的 Google 帳號：${accountEmail}`
              : '來信時請附上你登入用的 Google 帳號。'}
          </Typography>
        </Stack>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, justifyContent: 'center', gap: 1 }}>
        {SPONSOR_URL && (
          <Button
            href={SPONSOR_URL}
            target="_blank"
            rel="noopener noreferrer"
            variant="outlined"
            sx={{ px: 3, py: 1, borderRadius: 2 }}
          >
            贊助作者
          </Button>
        )}
        <Button
          onClick={handleClose}
          variant="contained"
          autoFocus
          sx={{ px: 4, py: 1, borderRadius: 2 }}
        >
          我知道了
        </Button>
      </DialogActions>
    </Dialog>
  );
}
