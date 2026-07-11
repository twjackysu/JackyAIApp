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
import { CreditTransaction } from '@/apis/creditApis/types';
import { useCreateECPayPaymentMutation, useGetECPayPacksQuery } from '@/apis/ecpayApis';
import {
  useCapturePayPalOrderMutation,
  useCreatePayPalOrderMutation,
  useGetPayPalPacksQuery,
} from '@/apis/paypalApis';
import { fmtNum } from '@/utils/format';

// ECPay (綠界) is the default — it's the Taiwan-local payment method and supports
// TW→TW payments, which PayPal cannot (PayPal is overseas-account-only).
type PaymentMethod = 'paypal' | 'ecpay';

const TRANSACTION_TYPE_LABELS: Record<CreditTransaction['transactionType'], string> = {
  initial: '初始贈送',
  consume: '消費',
  topup: '儲值',
  refund: '退款',
  bonus: '獎勵',
};

const Credits: React.FC = () => {
  const [searchParams] = useSearchParams();
  const [method, setMethod] = useState<PaymentMethod>('ecpay');
  const [snackbar, setSnackbar] = useState<{
    open: boolean;
    message: string;
    severity: 'success' | 'error' | 'info';
  }>({ open: false, message: '', severity: 'info' });

  const { data: balanceData, refetch: refetchBalance } = useGetCreditBalanceQuery();
  const { data: paypalPacks, isLoading: paypalLoading } = useGetPayPalPacksQuery();
  const { data: ecpayPacks, isLoading: ecpayLoading } = useGetECPayPacksQuery();

  const [createPayPalOrder, { isLoading: paypalOrderLoading }] = useCreatePayPalOrderMutation();
  const [capturePayPalOrder] = useCapturePayPalOrderMutation();
  const [createECPayPayment, { isLoading: ecpayOrderLoading }] = useCreateECPayPaymentMutation();

  // Guards the one-time handling of the payment-return query params (also avoids a
  // double PayPal capture under React StrictMode's double-invoked effects).
  const handledReturnRef = useRef(false);
  const [historyPage, setHistoryPage] = useState(1);
  const { data: historyData, refetch: refetchHistory } = useGetCreditHistoryQuery({
    pageNumber: historyPage,
    pageSize: 10,
  });

  // Handle return from PayPal / ECPay.
  useEffect(() => {
    const status = searchParams.get('status');
    if (!status || handledReturnRef.current) return;
    handledReturnRef.current = true;

    if (status === 'cancelled') {
      setSnackbar({ open: true, message: '付款已取消。', severity: 'info' });
      window.history.replaceState({}, '', '/credits');
      return;
    }

    if (status !== 'success') return;

    // Clean the URL up-front so a refresh can't re-trigger handling/capture.
    window.history.replaceState({}, '', '/credits');

    const onCredited = () => {
      setSnackbar({
        open: true,
        message: '付款成功！Credits 已入帳。',
        severity: 'success',
      });
      refetchBalance();
      refetchHistory();
    };

    // ECPay credits the account server-side via its own POST callback (/api/ecpay/callback)
    // before the browser is ever redirected back here — unlike PayPal, there's nothing left
    // for the client to "capture". Just confirm and refresh.
    if (searchParams.get('provider') === 'ecpay') {
      onCredited();
      return;
    }

    // PayPal Orders v2 is two-step: the buyer approved the order, now we must capture
    // it server-side — that's what actually charges and grants credits. PayPal appends
    // the order id as `token` on the return URL.
    const orderId = searchParams.get('token');
    if (!orderId) {
      setSnackbar({
        open: true,
        message: '無法驗證付款（缺少訂單編號）。',
        severity: 'error',
      });
      return;
    }
    setSnackbar({ open: true, message: '正在確認付款結果…', severity: 'info' });
    capturePayPalOrder(orderId)
      .unwrap()
      .then(onCredited)
      .catch(() => {
        setSnackbar({
          open: true,
          message: '無法完成 PayPal 付款確認，如已被扣款請聯絡客服。',
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
        message: '無法啟動結帳流程，請稍後再試一次。',
        severity: 'error',
      });
    }
  };

  const handleECPayPurchase = async (packId: string) => {
    try {
      const { PaymentGatewayUrl, ...fields } = await createECPayPayment(packId).unwrap();
      // ECPay's AioCheckOut expects a signed HTML form POST (CheckMacValue covers every
      // field), not a simple redirect — build and auto-submit a hidden form.
      const form = document.createElement('form');
      form.method = 'POST';
      form.action = PaymentGatewayUrl;
      Object.entries(fields).forEach(([key, value]) => {
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = key;
        input.value = value;
        form.appendChild(input);
      });
      document.body.appendChild(form);
      form.submit();
    } catch {
      setSnackbar({
        open: true,
        message: '無法啟動綠界付款，請稍後再試一次。',
        severity: 'error',
      });
    }
  };

  const packs =
    method === 'ecpay'
      ? ecpayPacks?.map((p) => ({
          id: p.id,
          name: p.name,
          price: p.priceTWD,
          credits: p.credits,
          badge: p.badge,
          currency: 'NT$',
        }))
      : paypalPacks?.map((p) => ({
          id: p.id,
          name: p.name,
          price: p.priceInCents / 100,
          credits: p.credits,
          badge: p.badge,
          currency: 'US$',
        }));

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Box sx={{ textAlign: 'center', mb: 3 }}>
        <Typography variant="h4" gutterBottom sx={{ fontWeight: 'bold' }}>
          購買 Credits
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 2 }}>
          Credits 可用於股票分析、對話測驗等 AI 功能。
        </Typography>
        {balanceData?.data && (
          <Chip
            label={`目前餘額：${fmtNum(balanceData.data.balance)} credits`}
            color="primary"
            variant="outlined"
            sx={{ fontSize: '1rem', py: 2, px: 1 }}
          />
        )}
      </Box>
      <Box sx={{ display: 'flex', justifyContent: 'center', mb: 2 }}>
        <Tabs value={method} onChange={(_, v) => setMethod(v as PaymentMethod)}>
          <Tab label="綠界 ECPay（台灣本地）" value="ecpay" />
          <Tab label="PayPal（僅限海外帳戶）" value="paypal" />
        </Tabs>
      </Box>

      {/* PayPal can't process Taiwan-domestic (TW→TW) payments, so ECPay is the default
          for TW users; PayPal stays available for overseas accounts. */}
      {method === 'paypal' && (
        <Alert severity="info" sx={{ mb: 3, maxWidth: 640, mx: 'auto' }}>
          PayPal 不支援台灣帳戶之間的付款（TW→TW），僅適用於海外 / 非台灣的 PayPal 帳戶。
          台灣用戶建議使用綠界 ECPay。
        </Alert>
      )}

      {(method === 'ecpay' ? ecpayLoading : paypalLoading) ? (
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
                    {fmtNum(pack.credits)} credits
                  </Typography>
                </CardContent>
                <CardActions sx={{ justifyContent: 'center', pb: 3 }}>
                  <Button
                    variant={pack.id === 'popular' ? 'contained' : 'outlined'}
                    size="large"
                    onClick={() =>
                      method === 'ecpay'
                        ? handleECPayPurchase(pack.id)
                        : handlePayPalPurchase(pack.id)
                    }
                    disabled={method === 'ecpay' ? ecpayOrderLoading : paypalOrderLoading}
                    sx={{ minWidth: 160 }}
                  >
                    {(method === 'ecpay' ? ecpayOrderLoading : paypalOrderLoading)
                      ? '處理中...'
                      : '立即購買'}
                  </Button>
                </CardActions>
              </Card>
            </Grid>
          ))}
        </Grid>
      )}
      <Box sx={{ textAlign: 'center', mt: 5 }}>
        <Typography variant="body2" color="text.secondary">
          付款皆採安全加密處理，付款完成後 Credits 立即入帳。
        </Typography>
      </Box>
      {/* Transaction History */}
      {historyData?.data && historyData.data.transactions.length > 0 && (
        <Box sx={{ mt: 6 }}>
          <Divider sx={{ mb: 3 }} />
          <Typography variant="h5" gutterBottom sx={{ fontWeight: 'bold' }}>
            交易紀錄
          </Typography>
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>日期</TableCell>
                  <TableCell>類型</TableCell>
                  <TableCell>原因</TableCell>
                  <TableCell align="right">金額</TableCell>
                  <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                    餘額
                  </TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {historyData.data.transactions.map((tx) => (
                  <TableRow key={tx.id}>
                    <TableCell>{new Date(tx.createdAt).toLocaleDateString()}</TableCell>
                    <TableCell>
                      <Chip
                        label={TRANSACTION_TYPE_LABELS[tx.transactionType] ?? tx.transactionType}
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
                      {tx.amount > 0 ? '+' : ''}
                      {tx.amount}
                    </TableCell>
                    <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                      {tx.balanceAfter}
                    </TableCell>
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
