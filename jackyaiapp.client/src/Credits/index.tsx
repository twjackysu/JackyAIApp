import ContentCopyIcon from '@mui/icons-material/ContentCopy';
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
  IconButton,
  Pagination,
  Paper,
  Snackbar,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tabs,
  Tooltip,
  Typography,
} from '@mui/material';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGetCreditBalanceQuery, useGetCreditHistoryQuery } from '@/apis/creditApis';
import { useGetECPayPacksQuery } from '@/apis/ecpayApis';
import {
  useCapturePayPalOrderMutation,
  useCreatePayPalOrderMutation,
  useGetPayPalPacksQuery,
} from '@/apis/paypalApis';

type PaymentMethod = 'banktransfer' | 'paypal';

// Manual bank-transfer receiving account (Taiwan-local payments).
const BANK_TRANSFER = {
  bankName: '台新銀行',
  bankCode: '812',
  account: '28881002207977',
};

const Credits: React.FC = () => {
  const [searchParams] = useSearchParams();
  // Bank transfer is the default — it's the only method that works for Taiwan-local
  // payers (PayPal cannot do TW→TW domestic transfers).
  const [method, setMethod] = useState<PaymentMethod>('banktransfer');
  const [selectedBankPackId, setSelectedBankPackId] = useState<string | null>(null);
  const [snackbar, setSnackbar] = useState<{
    open: boolean;
    message: string;
    severity: 'success' | 'error' | 'info';
  }>({ open: false, message: '', severity: 'info' });

  const { data: userInfo } = useGetUserInfoQuery();
  const { data: balanceData, refetch: refetchBalance } = useGetCreditBalanceQuery();
  const { data: paypalPacks, isLoading: paypalLoading } = useGetPayPalPacksQuery();
  const { data: ecpayPacks, isLoading: ecpayLoading } = useGetECPayPacksQuery();

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

  const userEmail = userInfo?.data?.email ?? '';

  // Handle return from payment provider (PayPal only — bank transfer never redirects).
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

  const handleCopyAccount = async () => {
    try {
      await navigator.clipboard.writeText(BANK_TRANSFER.account);
      setSnackbar({ open: true, message: '帳號已複製', severity: 'success' });
    } catch {
      setSnackbar({ open: true, message: '複製失敗，請手動選取帳號', severity: 'error' });
    }
  };

  const isBankTransfer = method === 'banktransfer';
  const isLoading = isBankTransfer ? ecpayLoading : paypalLoading;
  const isCheckoutLoading = paypalOrderLoading;

  // Bank transfer reuses the TWD-priced packs; PayPal uses USD-priced packs.
  const packs = isBankTransfer
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
        currency: '$',
      }));

  const selectedBankPack = packs?.find((p) => p.id === selectedBankPackId);

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
          <Tab label="銀行轉帳" value="banktransfer" />
          <Tab label="PayPal（僅限海外帳戶）" value="paypal" />
        </Tabs>
      </Box>

      {/* PayPal can't process Taiwan-domestic (TW→TW) payments — make that explicit. */}
      {method === 'paypal' && (
        <Alert severity="info" sx={{ mb: 3, maxWidth: 640, mx: 'auto' }}>
          PayPal 不支援台灣帳戶之間的付款（TW→TW），僅適用於海外 / 非台灣的 PayPal 帳戶。
          台灣使用者請改用「銀行轉帳」。
        </Alert>
      )}

      {/* Bank transfer instructions */}
      {isBankTransfer && (
        <Paper variant="outlined" sx={{ p: 3, mb: 3, maxWidth: 640, mx: 'auto' }}>
          <Typography variant="h6" sx={{ fontWeight: 'bold', mb: 1.5 }}>
            轉帳資訊
          </Typography>
          <Stack spacing={1} sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
              <Typography color="text.secondary">銀行</Typography>
              <Typography sx={{ fontWeight: 500 }}>
                {BANK_TRANSFER.bankName}（{BANK_TRANSFER.bankCode}）
              </Typography>
            </Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Typography color="text.secondary">帳號</Typography>
              <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
                <Typography sx={{ fontWeight: 600, fontFamily: 'monospace' }}>
                  {BANK_TRANSFER.account}
                </Typography>
                <Tooltip title="複製帳號">
                  <IconButton size="small" onClick={handleCopyAccount}>
                    <ContentCopyIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </Stack>
            </Box>
            {selectedBankPack && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                <Typography color="text.secondary">應轉金額</Typography>
                <Typography color="primary" sx={{ fontWeight: 700 }}>
                  NT${selectedBankPack.price}（{selectedBankPack.name}）
                </Typography>
              </Box>
            )}
          </Stack>
          <Divider sx={{ my: 1.5 }} />
          <Typography variant="body2" color="text.secondary" component="div">
            <ol style={{ margin: 0, paddingLeft: '1.2rem' }}>
              <li>在下方選擇方案，轉帳對應的金額到上方帳號。</li>
              <li>
                轉帳時請在「備註 / 附言」填入你的登入 email
                {userEmail ? (
                  <>
                    （<strong>{userEmail}</strong>）
                  </>
                ) : null}
                ，方便我們核對。
              </li>
              <li>
                我們會在 <strong>1～3 個工作天</strong> 內人工為你儲值，完成後此頁的餘額會更新。
              </li>
            </ol>
          </Typography>
        </Paper>
      )}

      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
          <CircularProgress />
        </Box>
      ) : (
        <Grid container spacing={3} sx={{ justifyContent: 'center' }}>
          {packs?.map((pack) => {
            const isSelected = isBankTransfer && pack.id === selectedBankPackId;
            return (
              <Grid key={pack.id} size={{ xs: 12, sm: 6, md: 4 }}>
                <Card
                  sx={{
                    height: '100%',
                    display: 'flex',
                    flexDirection: 'column',
                    border: isSelected
                      ? '2px solid #2e7d32'
                      : pack.id === 'popular'
                        ? '2px solid #1976d2'
                        : '1px solid #e0e0e0',
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
                    {isBankTransfer ? (
                      <Button
                        variant={isSelected ? 'contained' : 'outlined'}
                        color={isSelected ? 'success' : 'primary'}
                        size="large"
                        onClick={() => setSelectedBankPackId(pack.id)}
                        sx={{ minWidth: 160 }}
                      >
                        {isSelected ? '已選擇' : '選擇此方案'}
                      </Button>
                    ) : (
                      <Button
                        variant={pack.id === 'popular' ? 'contained' : 'outlined'}
                        size="large"
                        onClick={() => handlePayPalPurchase(pack.id)}
                        disabled={isCheckoutLoading}
                        sx={{ minWidth: 160 }}
                      >
                        {isCheckoutLoading ? 'Processing...' : 'Buy Now'}
                      </Button>
                    )}
                  </CardActions>
                </Card>
              </Grid>
            );
          })}
        </Grid>
      )}
      <Box sx={{ textAlign: 'center', mt: 5 }}>
        <Typography variant="body2" color="text.secondary">
          {isBankTransfer
            ? '銀行轉帳為人工儲值，約 1～3 個工作天完成。'
            : 'Payments are processed securely. Credits are added instantly after payment.'}
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
