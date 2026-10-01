import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Typography,
} from '@mui/material';

import { useAppDispatch, useAppSelector } from '@/redux/hooks';
import { closeUsageLimitModal } from '@/redux/uiSlice';

export default function UsageLimitModal() {
  const dispatch = useAppDispatch();
  const isUsageLimitModalOpen = useAppSelector((state) => state.ui.isUsageLimitModalOpen);

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
        <WarningAmberIcon color="warning" sx={{ fontSize: 48, mb: 1 }} />
        <Typography variant="h5" component="div" sx={{ fontWeight: 'bold' }}>
          已達使用量上限
        </Typography>
      </DialogTitle>
      <DialogContent sx={{ textAlign: 'center', pb: 2 }}>
        <Typography variant="body1" color="text.secondary">
          您已超出目前的使用量，如果您是重度使用者，請聯絡作者 jacky19918@gmail.com 幫您調高上限
        </Typography>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, justifyContent: 'center' }}>
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
