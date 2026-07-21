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
  Pagination,
  Snackbar,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { useGetCreditHistoryQuery } from '@/apis/creditApis';
import { CreditTransaction } from '@/apis/creditApis/types';
import {
  useCancelSubscriptionMutation,
  useCreateSubscriptionMutation,
  useGetSubscriptionStatusQuery,
} from '@/apis/payuniApis';
import { fmtNum } from '@/utils/format';

const TRANSACTION_TYPE_LABELS: Record<CreditTransaction['transactionType'], string> = {
  initial: '初始贈送',
  consume: '消費',
  topup: '儲值',
  refund: '退款',
  bonus: '獎勵',
  subscription_grant: '訂閱核發',
};

function SubscriptionCard() {
  const { data: status, isLoading, isFetching } = useGetSubscriptionStatusQuery();
  const [createSubscription, { isLoading: isCreating }] = useCreateSubscriptionMutation();
  const [cancelSubscription, { isLoading: isCancelling }] = useCancelSubscriptionMutation();
  const [error, setError] = useState<string | null>(null);

  const handleSubscribe = async () => {
    setError(null);
    try {
      const { PaymentGatewayUrl, ...fields } = await createSubscription().unwrap();
      // PayUni's UPP checkout expects a signed HTML form POST (HashInfo covers the encrypted
      // payload), not a simple redirect — build and auto-submit a hidden form.
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
      setError('無法啟動訂閱流程，請稍後再試一次。');
    }
  };

  const handleCancel = async () => {
    if (
      !window.confirm(
        '取消後，到期前仍可使用完整額度；到期後將不再視為訂閱中，需重新手動訂閱才能繼續使用。確定要取消嗎？',
      )
    )
      return;
    setError(null);
    try {
      await cancelSubscription().unwrap();
    } catch {
      setError('取消訂閱失敗，請稍後再試一次，或聯絡客服協助處理。');
    }
  };

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  const isActive = status?.status === 'active';
  const isCancelledButActive = status?.isSubscribed && status?.status === 'cancelled';

  return (
    <Card
      elevation={0}
      sx={{ maxWidth: 480, mx: 'auto', mb: 4, border: '2px solid', borderColor: 'primary.main' }}
    >
      <CardContent sx={{ textAlign: 'center', pt: 4 }}>
        <Typography variant="overline" color="text.secondary">
          Pro 訂閱
        </Typography>
        {status?.isSubscribed ? (
          <>
            <Chip
              label={isActive ? '訂閱中' : '已取消（到期前仍可使用）'}
              color={isActive ? 'success' : 'warning'}
              sx={{ my: 1, fontWeight: 'bold' }}
            />
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              {`訂閱到期日：${status.currentPeriodEnd ? new Date(status.currentPeriodEnd).toLocaleDateString() : '—'}（不會自動續訂，到期後請手動重新訂閱）`}
            </Typography>
            <Typography variant="h5" sx={{ fontWeight: 'bold', my: 1 }}>
              {fmtNum(status.currentBalance)} / {fmtNum(status.monthlyQuota)}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              本期剩餘使用額度{isFetching ? '（更新中…）' : ''}
            </Typography>
          </>
        ) : (
          <>
            <Typography variant="h3" sx={{ fontWeight: 'bold', my: 2 }}>
              NT$150
              <Typography component="span" variant="body1" color="text.secondary">
                {' '}
                / 月
              </Typography>
            </Typography>
            <Typography variant="body2" color="text.secondary">
              訂閱後解鎖全部功能，每月 {fmtNum(3000)} 點使用額度
            </Typography>
          </>
        )}
        {error && (
          <Alert severity="error" sx={{ mt: 2, textAlign: 'left' }}>
            {error}
          </Alert>
        )}
      </CardContent>
      <CardActions sx={{ justifyContent: 'center', pb: 3 }}>
        {isActive ? (
          <Button variant="outlined" color="error" onClick={handleCancel} disabled={isCancelling}>
            {isCancelling ? '處理中...' : '取消訂閱'}
          </Button>
        ) : isCancelledButActive ? null : (
          <Button variant="contained" size="large" onClick={handleSubscribe} disabled={isCreating}>
            {isCreating ? '處理中...' : '訂閱 Pro'}
          </Button>
        )}
      </CardActions>
    </Card>
  );
}

const Credits: React.FC = () => {
  const [searchParams] = useSearchParams();
  const [snackbar, setSnackbar] = useState<{
    open: boolean;
    message: string;
    severity: 'success' | 'error' | 'info';
  }>({ open: false, message: '', severity: 'info' });

  // Same query SubscriptionCard renders from — RTK Query dedupes identical queries across
  // components, this just gets us a `refetch` handle up here too.
  const { refetch: refetchSubscriptionStatus } = useGetSubscriptionStatusQuery();

  // Guards the one-time handling of the payment-return query params.
  const handledReturnRef = useRef(false);
  const [historyPage, setHistoryPage] = useState(1);
  const { data: historyData } = useGetCreditHistoryQuery({
    pageNumber: historyPage,
    pageSize: 10,
  });

  // Handle return from ECPay.
  useEffect(() => {
    const status = searchParams.get('status');
    if (!status || handledReturnRef.current) return;
    handledReturnRef.current = true;

    if (status === 'cancelled') {
      setSnackbar({ open: true, message: '付款已取消。', severity: 'info' });
      window.history.replaceState({}, '', '/credits');
      return;
    }

    if (status === 'failed') {
      setSnackbar({ open: true, message: '付款未成功，請稍後再試一次。', severity: 'error' });
      window.history.replaceState({}, '', '/credits');
      return;
    }

    if (status !== 'success') return;

    // Clean the URL up-front so a refresh can't re-trigger handling.
    window.history.replaceState({}, '', '/credits');

    // ECPay credits the account server-side via its own POST callback — which it fires before
    // ever redirecting the browser back here — so by the time this effect runs the grant has
    // already landed in the DB; just refetch so the (already-cached, now-stale) status query
    // picks it up.
    setSnackbar({
      open: true,
      message: '訂閱成立！使用額度已入帳。',
      severity: 'success',
    });
    refetchSubscriptionStatus();
  }, [searchParams, refetchSubscriptionStatus]);

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Box sx={{ textAlign: 'center', mb: 3 }}>
        <Typography variant="h4" gutterBottom sx={{ fontWeight: 'bold' }}>
          訂閱管理
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 2 }}>
          訂閱 Pro 即可使用股票分析、對話測驗等全部功能。
        </Typography>
      </Box>

      <SubscriptionCard />

      {/* Transaction History */}
      {historyData?.data && historyData.data.transactions.length > 0 && (
        <Box sx={{ mt: 6 }}>
          <Divider sx={{ mb: 3 }} />
          <Typography variant="h5" gutterBottom sx={{ fontWeight: 'bold' }}>
            使用紀錄
          </Typography>
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>日期</TableCell>
                  <TableCell>類型</TableCell>
                  <TableCell>原因</TableCell>
                  <TableCell align="right">數量</TableCell>
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
