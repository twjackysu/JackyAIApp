import {
  Alert,
  Box,
  Button,
  Card,
  CardActions,
  CardContent,
  Chip,
  CircularProgress,
  Grid,
  Snackbar,
  Typography,
} from '@mui/material';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { useGetCreditPacksQuery, useCreateCheckoutMutation } from '@/apis/stripeApis';
import { useGetCreditBalanceQuery } from '@/apis/creditApis';

const Credits: React.FC = () => {
  const [searchParams] = useSearchParams();
  const { data: packs, isLoading: packsLoading } = useGetCreditPacksQuery();
  const { data: balanceData, refetch: refetchBalance } = useGetCreditBalanceQuery();
  const [createCheckout, { isLoading: checkoutLoading }] = useCreateCheckoutMutation();
  const [snackbar, setSnackbar] = useState<{
    open: boolean;
    message: string;
    severity: 'success' | 'error' | 'info';
  }>({ open: false, message: '', severity: 'info' });

  // Handle return from Stripe
  useEffect(() => {
    const status = searchParams.get('status');
    if (status === 'success') {
      setSnackbar({ open: true, message: 'Payment successful! Credits have been added.', severity: 'success' });
      refetchBalance();
      window.history.replaceState({}, '', '/credits');
    } else if (status === 'cancelled') {
      setSnackbar({ open: true, message: 'Payment was cancelled.', severity: 'info' });
      window.history.replaceState({}, '', '/credits');
    }
  }, [searchParams, refetchBalance]);

  const handlePurchase = async (packId: string) => {
    try {
      const result = await createCheckout(packId).unwrap();
      window.location.href = result.checkoutUrl;
    } catch {
      setSnackbar({ open: true, message: 'Failed to start checkout. Please try again.', severity: 'error' });
    }
  };

  if (packsLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Box sx={{ textAlign: 'center', mb: 5 }}>
        <Typography variant="h4" fontWeight="bold" gutterBottom>
          Buy Credits
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 2 }}>
          Credits power AI features like stock analysis, conversation tests, and more.
        </Typography>
        {balanceData?.Data && (
          <Chip
            label={`Current Balance: ${balanceData.Data.balance.toLocaleString()} credits`}
            color="primary"
            variant="outlined"
            sx={{ fontSize: '1rem', py: 2, px: 1 }}
          />
        )}
      </Box>

      <Grid container spacing={3} justifyContent="center">
        {packs?.map((pack) => (
          <Grid item xs={12} sm={6} md={4} key={pack.id}>
            <Card
              sx={{
                height: '100%',
                display: 'flex',
                flexDirection: 'column',
                border: pack.id === 'popular' ? '2px solid #1976d2' : '1px solid #e0e0e0',
                position: 'relative',
                transition: 'transform 0.2s',
                '&:hover': { transform: 'translateY(-4px)', boxShadow: 4 },
              }}
            >
              {pack.badge && (
                <Chip
                  label={pack.badge}
                  color={pack.id === 'bestvalue' ? 'success' : 'primary'}
                  size="small"
                  sx={{
                    position: 'absolute',
                    top: 12,
                    right: 12,
                    fontWeight: 'bold',
                  }}
                />
              )}
              <CardContent sx={{ flexGrow: 1, textAlign: 'center', pt: 4 }}>
                <Typography variant="h6" gutterBottom>
                  {pack.name}
                </Typography>
                <Typography variant="h3" fontWeight="bold" color="primary" sx={{ my: 2 }}>
                  ${(pack.priceInCents / 100).toFixed(0)}
                </Typography>
                <Typography variant="h5" color="text.secondary">
                  {pack.credits.toLocaleString()} credits
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                  ${(pack.priceInCents / pack.credits).toFixed(1)}¢ per credit
                </Typography>
              </CardContent>
              <CardActions sx={{ justifyContent: 'center', pb: 3 }}>
                <Button
                  variant={pack.id === 'popular' ? 'contained' : 'outlined'}
                  size="large"
                  onClick={() => handlePurchase(pack.id)}
                  disabled={checkoutLoading}
                  sx={{ minWidth: 160 }}
                >
                  {checkoutLoading ? 'Processing...' : 'Buy Now'}
                </Button>
              </CardActions>
            </Card>
          </Grid>
        ))}
      </Grid>

      <Box sx={{ textAlign: 'center', mt: 5 }}>
        <Typography variant="body2" color="text.secondary">
          Payments are processed securely by Stripe. Credits are added instantly after payment.
        </Typography>
      </Box>

      <Snackbar
        open={snackbar.open}
        autoHideDuration={6000}
        onClose={() => setSnackbar((prev) => ({ ...prev, open: false }))}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          onClose={() => setSnackbar((prev) => ({ ...prev, open: false }))}
          severity={snackbar.severity}
          variant="filled"
        >
          {snackbar.message}
        </Alert>
      </Snackbar>
    </Box>
  );
};

export default Credits;
