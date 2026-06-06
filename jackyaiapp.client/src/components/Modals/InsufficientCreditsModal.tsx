import MonetizationOnIcon from '@mui/icons-material/MonetizationOn';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Typography,
  Box,
} from '@mui/material';
import { useNavigate } from 'react-router-dom';

import { useGetCreditBalanceQuery } from '@/apis/creditApis';
import { useAppDispatch, useAppSelector } from '@/redux/hooks';
import { closeCreditModal } from '@/redux/uiSlice';

export default function InsufficientCreditsModal() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();

  const { isCreditModalOpen, requiredCredits } = useAppSelector((state) => state.ui);

  // Try to use cached data or fetch silently, so we don't trigger unnecessary loaders if we already have it
  const { data: balanceData } = useGetCreditBalanceQuery(undefined, { skip: !isCreditModalOpen });
  const currentBalance = balanceData?.data?.balance ?? 0;

  const handleClose = () => {
    dispatch(closeCreditModal());
  };

  const handlePurchase = () => {
    dispatch(closeCreditModal());
    navigate('/credits');
  };

  return (
    <Dialog
      open={isCreditModalOpen}
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
        <Typography
          variant="h5"
          component="div"
          sx={{
            fontWeight: 'bold',
          }}
        >
          額度不足 (Insufficient Credits)
        </Typography>
      </DialogTitle>
      <DialogContent sx={{ textAlign: 'center', pb: 2 }}>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
          您目前沒有足夠的積分來使用此 AI 功能。請購買更多積分以繼續。
        </Typography>

        <Box
          sx={{
            display: 'flex',
            justifyContent: 'center',
            gap: 4,
            bgcolor: 'background.default',
            p: 2,
            borderRadius: 2,
            mb: 1,
          }}
        >
          <Box>
            <Typography variant="body2" color="text.secondary">
              目前餘額
            </Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', mt: 0.5 }}>
              <MonetizationOnIcon color="primary" fontSize="small" sx={{ mr: 0.5 }} />
              <Typography
                variant="h6"
                sx={{
                  fontWeight: 'bold',
                }}
              >
                {currentBalance.toLocaleString()}
              </Typography>
            </Box>
          </Box>

          {requiredCredits && (
            <Box>
              <Typography variant="body2" color="text.secondary">
                需要額度
              </Typography>
              <Box
                sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', mt: 0.5 }}
              >
                <MonetizationOnIcon color="error" fontSize="small" sx={{ mr: 0.5 }} />
                <Typography
                  variant="h6"
                  color="error.main"
                  sx={{
                    fontWeight: 'bold',
                  }}
                >
                  {requiredCredits.toLocaleString()}
                </Typography>
              </Box>
            </Box>
          )}
        </Box>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, justifyContent: 'center', gap: 2 }}>
        <Button onClick={handleClose} color="inherit" variant="text" sx={{ px: 3 }}>
          取消
        </Button>
        <Button
          onClick={handlePurchase}
          variant="contained"
          color="primary"
          autoFocus
          sx={{ px: 4, py: 1, borderRadius: 2 }}
        >
          前往購買 (Buy Credits)
        </Button>
      </DialogActions>
    </Dialog>
  );
}
