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
  Tab,
  Tabs,
  Typography,
} from '@mui/material';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { useGetCreditBalanceQuery } from '@/apis/creditApis';
import { useCreateECPayPaymentMutation, useGetECPayPacksQuery } from '@/apis/ecpayApis';
import { useCreatePayPalOrderMutation, useGetPayPalPacksQuery } from '@/apis/paypalApis';
import { useCreateCheckoutMutation, useGetCreditPacksQuery } from '@/apis/stripeApis';

type PaymentMethod = 'stripe' | 'paypal' | 'ecpay';

const Credits: React.FC = () => {
  const [searchParams] = useSearchParams();
  const [method, setMethod] = useState<PaymentMethod>('paypal');
  const [snackbar, setSnackbar] = useState<{
    open: boolean;
    message: string;
    severity: 'success' | 'error' | 'info';
  }>({ open: false, message: '', severity: 'info' });

  const { data: balanceData, refetch: refetchBalance } = useGetCreditBalanceQuery();
  const { data: stripePacks, isLoading: stripeLoading } = useGetCreditPacksQuery();
  const { data: paypalPacks, isLoading: paypalLoading } = useGetPayPalPacksQuery();
  const { data: ecpayPacks, isLoading: ecpayLoading } = useGetECPayPacksQuery();

  const [createStripeCheckout, { isLoading: stripeCheckoutLoading }] = useCreateCheckoutMutation();
  const [createPayPalOrder, { isLoading: paypalOrderLoading }] = useCreatePayPalOrderMutation();
  const [createECPayPayment, { isLoading: ecpayPaymentLoading }] = useCreateECPayPaymentMutation();

  const ecpayFormRef = useRef<HTMLFormElement>(null);

  // Handle return from payment provider
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
      if (method === 'stripe') {
        const result = await createStripeCheckout(packId).unwrap();
        window.location.href = result.checkoutUrl;
      } else if (method === 'paypal') {
        const result = await createPayPalOrder(packId).unwrap();
        window.location.href = result.approveUrl;
      } else if (method === 'ecpay') {
        const result = await createECPayPayment(packId).unwrap();
        // ECPay requires form POST — build and submit a hidden form
        const gatewayUrl = result['PaymentGatewayUrl'];
        if (!gatewayUrl) throw new Error('Missing payment gateway URL');

        const form = ecpayFormRef.current;
        if (!form) return;
        form.action = gatewayUrl;
        form.innerHTML = '';
        Object.entries(result).forEach(([key, value]) => {
          if (key === 'PaymentGatewayUrl') return;
          const input = document.createElement('input');
          input.type = 'hidden';
          input.name = key;
          input.value = value;
          form.appendChild(input);
        });
        form.submit();
      }
    } catch {
      setSnackbar({ open: true, message: 'Failed to start checkout. Please try again.', severity: 'error' });
    }
  };

  const isLoading = method === 'stripe' ? stripeLoading : method === 'paypal' ? paypalLoading : ecpayLoading;
  const isCheckoutLoading = stripeCheckoutLoading || paypalOrderLoading || ecpayPaymentLoading;
  const isECPay = method === 'ecpay';

  // Normalize packs for display
  const packs = method === 'ecpay'
    ? ecpayPacks?.map((p) => ({ id: p.id, name: p.name, price: p.priceTWD, credits: p.credits, badge: p.badge, currency: 'NT$' }))
    : (method === 'stripe' ? stripePacks : paypalPacks)?.map((p) => ({
        id: p.id, name: p.name, price: p.priceInCents / 100, credits: p.credits, badge: p.badge, currency: '$',
      }));

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Box sx={{ textAlign: 'center', mb: 3 }}>
        <Typography variant="h4" fontWeight="bold" gutterBottom>
          Buy Credits
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 2 }}>
          Credits power AI features like stock analysis, conversation tests, and more.
        </Typography>
        {balanceData?.data && (
          <Chip
            label={`Current Balance: ${balanceData.data.balance.toLocaleString()} credits`}
            color="primary"
            variant="outlined"
            sx={{ fontSize: '1rem', py: 2, px: 1 }}
          />
        )}
      </Box>

      <Box sx={{ display: 'flex', justifyContent: 'center', mb: 4 }}>
        <Tabs value={method} onChange={(_, v) => setMethod(v as PaymentMethod)}>
          <Tab label="PayPal" value="paypal" />
          <Tab label="綠界 ECPay" value="ecpay" />
          <Tab label="Stripe" value="stripe" />
        </Tabs>
      </Box>

      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
          <CircularProgress />
        </Box>
      ) : (
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
                    sx={{ position: 'absolute', top: 12, right: 12, fontWeight: 'bold' }}
                  />
                )}
                <CardContent sx={{ flexGrow: 1, textAlign: 'center', pt: 4 }}>
                  <Typography variant="h6" gutterBottom>{pack.name}</Typography>
                  <Typography variant="h3" fontWeight="bold" color="primary" sx={{ my: 2 }}>
                    {pack.currency}{pack.price}
                  </Typography>
                  <Typography variant="h5" color="text.secondary">
                    {pack.credits.toLocaleString()} credits
                  </Typography>
                </CardContent>
                <CardActions sx={{ justifyContent: 'center', pb: 3 }}>
                  <Button
                    variant={pack.id === 'popular' ? 'contained' : 'outlined'}
                    size="large"
                    onClick={() => handlePurchase(pack.id)}
                    disabled={isCheckoutLoading}
                    sx={{ minWidth: 160 }}
                  >
                    {isCheckoutLoading ? 'Processing...' : 'Buy Now'}
                  </Button>
                </CardActions>
              </Card>
            </Grid>
          ))}
        </Grid>
      )}

      <Box sx={{ textAlign: 'center', mt: 5 }}>
        <Typography variant="body2" color="text.secondary">
          {isECPay
            ? '由綠界科技安全處理付款，支援信用卡。Credits 將於付款完成後立即入帳。'
            : 'Payments are processed securely. Credits are added instantly after payment.'}
        </Typography>
      </Box>

      {/* Hidden form for ECPay POST */}
      <form ref={ecpayFormRef} method="POST" style={{ display: 'none' }} />

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
