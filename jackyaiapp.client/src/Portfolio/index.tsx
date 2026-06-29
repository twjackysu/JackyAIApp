import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import Grid from '@mui/material/Grid';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';

import { HoldingsTable } from './HoldingsTable';
import { TransactionForm } from './TransactionForm';

import { useGetPortfolioSummaryQuery } from '@/apis/portfolioApis';

const fmt = (n: number) =>
  n.toLocaleString('zh-TW', { minimumFractionDigits: 0, maximumFractionDigits: 0 });

const pnlColor = (n: number) => (n > 0 ? '#22c55e' : n < 0 ? '#ef4444' : 'inherit');

function NetWorthCard({ total, pnl, pnlPct }: { total: number; pnl: number; pnlPct: number }) {
  const isPositive = pnl >= 0;
  return (
    <Card
      elevation={0}
      sx={{
        borderRadius: 3,
        border: '1px solid',
        borderColor: 'divider',
        background: (theme) =>
          `linear-gradient(135deg, ${alpha('#F59E0B', 0.12)} 0%, ${alpha(
            theme.palette.background.paper,
            0.4,
          )} 60%)`,
      }}
    >
      <CardContent sx={{ p: 3 }}>
        <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 2 }}>
          總資產淨值
        </Typography>
        <Typography variant="h3" sx={{ fontWeight: 800, mt: 0.5, letterSpacing: -1 }}>
          NT${fmt(total)}
        </Typography>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1 }}>
          <Typography
            variant="body1"
            sx={{ color: pnlColor(pnl), fontWeight: 600 }}
            component="span"
          >
            {isPositive ? '▲' : '▼'} NT${fmt(Math.abs(pnl))}
          </Typography>
          <Chip
            label={`${isPositive ? '+' : ''}${pnlPct.toFixed(2)}%`}
            size="small"
            sx={{
              bgcolor: alpha(isPositive ? '#22c55e' : '#ef4444', 0.15),
              color: isPositive ? '#22c55e' : '#ef4444',
              fontWeight: 700,
              border: 'none',
            }}
          />
          <Typography variant="caption" color="text.secondary">
            未實現損益
          </Typography>
        </Box>
      </CardContent>
    </Card>
  );
}

function AllocationCard({
  twStock,
  usStock,
  total,
  usdTwdRate,
}: {
  twStock: number;
  usStock: number;
  total: number;
  usdTwdRate: number;
}) {
  const twPct = total > 0 ? (twStock / total) * 100 : 0;
  const usPct = total > 0 ? (usStock / total) * 100 : 0;

  return (
    <Card
      elevation={0}
      sx={{ borderRadius: 3, border: '1px solid', borderColor: 'divider', height: '100%' }}
    >
      <CardContent sx={{ p: 3 }}>
        <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 2 }}>
          資產配置
        </Typography>
        <Box sx={{ mt: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
          <AllocationRow label="台股" value={twStock} pct={twPct} color="#3B82F6" />
          <AllocationRow label="美股" value={usStock} pct={usPct} color="#F59E0B" />
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ mt: 2, display: 'block' }}>
          USD/TWD = {usdTwdRate.toFixed(2)}
        </Typography>
      </CardContent>
    </Card>
  );
}

function AllocationRow({
  label,
  value,
  pct,
  color,
}: {
  label: string;
  value: number;
  pct: number;
  color: string;
}) {
  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          {label}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          NT${fmt(value)} ({pct.toFixed(1)}%)
        </Typography>
      </Box>
      <Box
        sx={{
          height: 6,
          borderRadius: 3,
          bgcolor: alpha(color, 0.15),
          overflow: 'hidden',
        }}
      >
        <Box
          sx={{
            height: '100%',
            width: `${pct}%`,
            bgcolor: color,
            borderRadius: 3,
            transition: 'width 0.6s ease',
          }}
        />
      </Box>
    </Box>
  );
}

export default function Portfolio() {
  const { data: summaryRes, isLoading, isError } = useGetPortfolioSummaryQuery();
  const summary = summaryRes?.data;

  return (
    <Box sx={{ maxWidth: 1100, mx: 'auto', py: 3 }}>
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 3 }}>
        <Box>
          <Typography variant="h5" sx={{ fontWeight: 700 }}>
            My Portfolio
          </Typography>
          <Typography variant="body2" color="text.secondary">
            台股 · 美股持倉追蹤
          </Typography>
        </Box>
        <TransactionForm />
      </Box>

      {isLoading && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
          <CircularProgress />
        </Box>
      )}

      {isError && (
        <Typography color="error" sx={{ py: 4, textAlign: 'center' }}>
          載入失敗，請重新整理
        </Typography>
      )}

      {!isLoading && summary && (
        <>
          {/* Summary Cards */}
          <Grid container spacing={2} sx={{ mb: 3 }}>
            <Grid size={{ xs: 12, md: 8 }}>
              <NetWorthCard
                total={summary.totalNetWorthTWD}
                pnl={summary.totalUnrealizedPnlTWD}
                pnlPct={summary.totalUnrealizedPnlPct}
              />
            </Grid>
            <Grid size={{ xs: 12, md: 4 }}>
              <AllocationCard
                twStock={summary.twStockTWD}
                usStock={summary.usStockTWD}
                total={summary.totalNetWorthTWD}
                usdTwdRate={summary.usdTwdRate}
              />
            </Grid>
          </Grid>

          {/* Holdings */}
          <Card elevation={0} sx={{ borderRadius: 3, border: '1px solid', borderColor: 'divider' }}>
            <CardContent sx={{ p: 3 }}>
              <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
                持倉明細
              </Typography>
              <Divider sx={{ mb: 2 }} />
              <HoldingsTable holdings={summary.holdings} usdTwdRate={summary.usdTwdRate} />
            </CardContent>
          </Card>
        </>
      )}

      {/* Empty state (logged in but no holdings yet) */}
      {!isLoading && !isError && summary && summary.holdings.length === 0 && (
        <Box sx={{ textAlign: 'center', py: 4 }}>
          <Typography color="text.secondary">尚無持倉。點「新增交易」記錄第一筆交易。</Typography>
        </Box>
      )}
    </Box>
  );
}
