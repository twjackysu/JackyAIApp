import {
  Alert,
  Box,
  Button,
  Card,
  CardActions,
  CardContent,
  Chip,
  CircularProgress,
  Divider,
  Grid,
  Pagination,
  Snackbar,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tabs,
  Typography,
} from '@mui/material';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { useGetCreditBalanceQuery, useGetCreditHistoryQuery } from '@/apis/creditApis';
import {
  useCapturePayPalOrderMutation,
  useCreatePayPalOrderMutation,
  useGetPayPalPacksQuery,
} from '@/apis/paypalApis';

// 'ecpay' is a disabled placeholder until a real ECPay merchant account is set up —
// that will be the Taiwan-local payment method. PayPal is overseas-only.
type PaymentMethod = 'paypal' | 'ecpay';

const Credits: React.FC = () => {
  const [searchParams] = useSearchParams();
  const [method, setMethod] = useState<PaymentMethod>('paypal');
  const [snackbar, setSnackbar] = useState<{
    open: boolean;
    message: string;
    severity: 'success' | 'error' | 'info';
  }>({ open: false, message: '', severity: 'info' });

  const { data: balanceData, refetch: refetchBalance } = useGetCreditBalanceQuery();
  const { data: paypalPacks, isLoading: paypalLoading } = useGetPayPalPacksQuery();

  const [createPayPalOrder, { isLoading: paypalOrderLoading }] = useCreatePayPalOrderMutation();
  const [capturePayPalOrder] = useCapturePayPalOrderMutation();

  // Guards the one-time handling of the payment-return query params (also avoids a
  // double PayPal capture under React StrictMode's double-invoked effects).
  const handledReturnRef = useRef(false);
  const [historyPage, setHistoryPage] = useState(1);
  const { data: historyData, refetch: refetchHistory } = useGetCreditHistoryQuery({
    pageNumber: historyPage,
    pageSize: 10,
  });

  // Handle return from PayPal.
  useEffect(() => {
    const status = searchParams.get('status');
    if (!status || handledReturnRef.current) return;
    handledReturnRef.current = true;

    if (status === 'cancelled') {
      setSnackbar({ open: true, message: 'Payment was cancelled.', severity: 'info' });
      window.history.replaceState({}, '', '/credits');
      return;
    }

    if (status !== 'success') return;

    // Clean the URL up-front so a refresh can't re-trigger handling/capture.
    window.history.replaceState({}, '', '/credits');

    const onCredited = () => {
      setSnackbar({
        open: true,
        message: 'Payment successful! Credits have been added.',
        severity: 'success',
      });
      refetchBalance();
      refetchHistory();
    };

    // PayPal Orders v2 is two-step: the buyer approved the order, now we must capture
    // it server-side — that's what actually charges and grants credits. PayPal appends
    // the order id as `token` on the return URL.
    const orderId = searchParams.get('token');
    if (!orderId) {
      setSnackbar({
        open: true,
        message: 'Payment could not be verified (missing order id).',
        severity: 'error',
      });
      return;
    }
    setSnackbar({ open: true, message: 'Finalizing your payment…', severity: 'info' });
    capturePayPalOrder(orderId)
      .unwrap()
      .then(onCredited)
      .catch(() => {
        setSnackbar({
          open: true,
          message:
            'We could not finalize your PayPal payment. If you were charged, please contact support.',
          severity: 'error',
        });
      });
  }, [searchParams, refetchBalance, refetchHistory, capturePayPalOrder]);

  const handlePayPalPurchase = async (packId: string) => {
    try {
      const result = await createPayPalOrder(packId).unwrap();
      window.location.href = result.approveUrl;
    } catch {
      setSnackbar({
        open: true,
        message: 'Failed to start checkout. Please try again.',
        severity: 'error',
      });
    }
  };

  const packs = paypalPacks?.map((p) => ({
    id: p.id,
    name: p.name,
    price: p.priceInCents / 100,
    credits: p.credits,
    badge: p.badge,
    currency: '$',
  }));

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Box sx={{ textAlign: 'center', mb: 3 }}>
        <Typography variant="h4" gutterBottom sx={{ fontWeight: 'bold' }}>
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
      <Box sx={{ display: 'flex', justifyContent: 'center', mb: 2 }}>
        <Tabs value={method} onChange={(_, v) => setMethod(v as PaymentMethod)}>
          <Tab label="PayPal（僅限海外帳戶）" value="paypal" />
          <Tab label="綠界 ECPay（即將開放）" value="ecpay" disabled />
        </Tabs>
      </Box>

      {/* PayPal can't process Taiwan-domestic (TW→TW) payments — make that explicit, and
          point Taiwan users at the upcoming local option. */}
      <Alert severity="info" sx={{ mb: 3, maxWidth: 640, mx: 'auto' }}>
        PayPal 不支援台灣帳戶之間的付款（TW→TW），僅適用於海外 / 非台灣的 PayPal 帳戶。
        台灣本地付款方式（綠界 ECPay）即將開放。
      </Alert>

      {paypalLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
          <CircularProgress />
        </Box>
      ) : (
        <Grid container spacing={3} sx={{ justifyContent: 'center' }}>
          {packs?.map((pack) => (
            <Grid key={pack.id} size={{ xs: 12, sm: 6, md: 4 }}>
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
                  <Typography variant="h6" gutterBottom>
                    {pack.name}
                  </Typography>
                  <Typography variant="h3" color="primary" sx={{ fontWeight: 'bold', my: 2 }}>
                    {pack.currency}
                    {pack.price}
                  </Typography>
                  <Typography variant="h5" color="text.secondary">
                    {pack.credits.toLocaleString()} credits
                  </Typography>
                </CardContent>
                <CardActions sx={{ justifyContent: 'center', pb: 3 }}>
                  <Button
                    variant={pack.id === 'popular' ? 'contained' : 'outlined'}
                    size="large"
                    onClick={() => handlePayPalPurchase(pack.id)}
                    disabled={paypalOrderLoading}
                    sx={{ minWidth: 160 }}
                  >
                    {paypalOrderLoading ? 'Processing...' : 'Buy Now'}
                  </Button>
                </CardActions>
              </Card>
            </Grid>
          ))}
        </Grid>
      )}
      <Box sx={{ textAlign: 'center', mt: 5 }}>
        <Typography variant="body2" color="text.secondary">
          Payments are processed securely. Credits are added instantly after payment.
        </Typography>
      </Box>
      {/* Transaction History */}
      {historyData?.data && historyData.data.transactions.length > 0 && (
        <Box sx={{ mt: 6 }}>
          <Divider sx={{ mb: 3 }} />
          <Typography variant="h5" gutterBottom sx={{ fontWeight: 'bold' }}>
            Transaction History
          </Typography>
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Date</TableCell>
                  <TableCell>Type</TableCell>
                  <TableCell>Reason</TableCell>
                  <TableCell align="right">Amount</TableCell>
                  <TableCell align="right">Balance</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {historyData.data.transactions.map((tx) => (
                  <TableRow key={tx.id}>
                    <TableCell>{new Date(tx.createdAt).toLocaleDateString()}</TableCell>
                    <TableCell>
                      <Chip
                        label={tx.transactionType}
                        size="small"
                        color={
                          tx.transactionType === 'consume'
                            ? 'error'
                            : tx.transactionType === 'initial'
                              ? 'default'
                              : 'success'
                        }
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>{tx.reason}</TableCell>
                    <TableCell
                      align="right"
                      sx={{
                        color: tx.transactionType === 'consume' ? 'error.main' : 'success.main',
                        fontWeight: 'bold',
                      }}
                    >
                      {tx.transactionType === 'consume' ? '-' : '+'}
                      {tx.amount}
                    </TableCell>
                    <TableCell align="right">{tx.balanceAfter}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
          {historyData.data.pagination.totalPages > 1 && (
            <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2 }}>
              <Pagination
                count={historyData.data.pagination.totalPages}
                page={historyPage}
                onChange={(_, page) => setHistoryPage(page)}
              />
            </Box>
          )}
        </Box>
      )}
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
