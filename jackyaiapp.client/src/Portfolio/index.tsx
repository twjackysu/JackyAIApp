import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import Grid from '@mui/material/Grid';
import IconButton from '@mui/material/IconButton';
import { alpha } from '@mui/material/styles';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { HoldingDetailDrawer } from './HoldingDetailDrawer';
import { HoldingsEditor } from './HoldingsEditor';
import { NetWorthChart } from './NetWorthChart';
import { PositionsTable } from './PositionsTable';
import { usePrivacy } from './privacy';
import { PrivacyProvider } from './PrivacyContext';

import { useGetPortfolioSummaryQuery } from '@/apis/portfolioApis';
import { PositionDto } from '@/apis/portfolioApis/types';

const fmt = (n: number) =>
  n.toLocaleString('zh-TW', { minimumFractionDigits: 0, maximumFractionDigits: 0 });

const pnlColor = (n: number) => (n > 0 ? '#22c55e' : n < 0 ? '#ef4444' : 'inherit');

function PrivacyToggle() {
  const { hidden, toggle } = usePrivacy();
  return (
    <Tooltip title={hidden ? '顯示金額' : '隱藏金額'}>
      <IconButton onClick={toggle} size="small">
        {hidden ? <VisibilityOffIcon /> : <VisibilityIcon />}
      </IconButton>
    </Tooltip>
  );
}

function NetWorthCard({
  total,
  pnl,
  pnlPct,
  hasPnl,
}: {
  total: number;
  pnl: number;
  pnlPct: number;
  hasPnl: boolean;
}) {
  const isPositive = pnl >= 0;
  const { money } = usePrivacy();
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
          NT${money(fmt(total))}
        </Typography>
        {hasPnl && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1 }}>
            <Typography
              variant="body1"
              sx={{ color: pnlColor(pnl), fontWeight: 600 }}
              component="span"
            >
              {isPositive ? '▲' : '▼'} NT${money(fmt(Math.abs(pnl)))}
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
              未實現損益(已填成本部分)
            </Typography>
          </Box>
        )}
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
  const { money } = usePrivacy();
  if (value <= 0) return null;
  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          {label}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          NT${money(fmt(value))} ({pct.toFixed(1)}%)
        </Typography>
      </Box>
      <Box sx={{ height: 6, borderRadius: 3, bgcolor: alpha(color, 0.15), overflow: 'hidden' }}>
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

function AllocationCard({
  twStock,
  usStock,
  cash,
  fund,
  total,
  usdTwdRate,
}: {
  twStock: number;
  usStock: number;
  cash: number;
  fund: number;
  total: number;
  usdTwdRate: number;
}) {
  const pct = (v: number) => (total > 0 ? (v / total) * 100 : 0);
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
          <AllocationRow label="台股" value={twStock} pct={pct(twStock)} color="#3B82F6" />
          <AllocationRow label="美股" value={usStock} pct={pct(usStock)} color="#10B981" />
          <AllocationRow label="現金" value={cash} pct={pct(cash)} color="#A78BFA" />
          <AllocationRow label="基金" value={fund} pct={pct(fund)} color="#EC4899" />
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ mt: 2, display: 'block' }}>
          USD/TWD = {usdTwdRate.toFixed(2)}
        </Typography>
      </CardContent>
    </Card>
  );
}

export default function Portfolio() {
  const { data: summaryRes, isLoading, isError } = useGetPortfolioSummaryQuery();
  const summary = summaryRes?.data;
  const [selected, setSelected] = useState<PositionDto | null>(null);

  const hasHoldings = (summary?.positions?.length ?? 0) > 0;
  const hasPnl = (summary?.costBasisTWD ?? 0) > 0;

  return (
    <PrivacyProvider>
      <Box sx={{ maxWidth: 1100, mx: 'auto', py: 3 }}>
        {/* Header */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 3 }}>
          <Box>
            <Typography variant="h5" sx={{ fontWeight: 700 }}>
              My Portfolio
            </Typography>
            <Typography variant="body2" color="text.secondary">
              台股 · 美股 · 現金 · 基金 統一以台幣淨值追蹤
              {summary?.effectiveDate ? ` · 持倉生效日 ${summary.effectiveDate}` : ''}
            </Typography>
          </Box>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <PrivacyToggle />
            <HoldingsEditor />
          </Box>
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

        {!isLoading && summary && hasHoldings && (
          <>
            <Grid container spacing={2} sx={{ mb: 3 }}>
              <Grid size={{ xs: 12, md: 8 }}>
                <NetWorthCard
                  total={summary.totalNetWorthTWD}
                  pnl={summary.unrealizedPnlTWD}
                  pnlPct={summary.unrealizedPnlPct}
                  hasPnl={hasPnl}
                />
              </Grid>
              <Grid size={{ xs: 12, md: 4 }}>
                <AllocationCard
                  twStock={summary.twStockTWD}
                  usStock={summary.usStockTWD}
                  cash={summary.cashTWD}
                  fund={summary.fundTWD}
                  total={summary.totalNetWorthTWD}
                  usdTwdRate={summary.usdTwdRate}
                />
              </Grid>
            </Grid>

            <Card
              elevation={0}
              sx={{ borderRadius: 3, border: '1px solid', borderColor: 'divider', mb: 3 }}
            >
              <CardContent sx={{ p: 3 }}>
                <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
                  資產淨值走勢
                </Typography>
                <Divider sx={{ mb: 2 }} />
                <NetWorthChart />
              </CardContent>
            </Card>

            <Card
              elevation={0}
              sx={{ borderRadius: 3, border: '1px solid', borderColor: 'divider' }}
            >
              <CardContent sx={{ p: 2 }}>
                <PositionsTable positions={summary.positions} onStockClick={setSelected} />
              </CardContent>
            </Card>
          </>
        )}

        {!isLoading && !isError && summary && !hasHoldings && (
          <Box sx={{ textAlign: 'center', py: 8 }}>
            <Typography color="text.secondary" sx={{ mb: 1 }}>
              還沒有持倉紀錄
            </Typography>
            <Typography variant="body2" color="text.secondary">
              點右上「建立持倉」填入你的股票、現金和基金
            </Typography>
          </Box>
        )}

        <HoldingDetailDrawer
          position={selected}
          open={selected !== null}
          onClose={() => setSelected(null)}
        />
      </Box>
    </PrivacyProvider>
  );
}
