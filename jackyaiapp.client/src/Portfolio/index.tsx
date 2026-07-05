import FileDownloadOutlinedIcon from '@mui/icons-material/FileDownloadOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChart';
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
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import { alpha } from '@mui/material/styles';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { HoldingDetailDrawer } from './HoldingDetailDrawer';
import { HoldingsEditor } from './HoldingsEditor';
import { NetWorthChart } from './NetWorthChart';
import { exportNetWorthCsv, exportPortfolioCsv } from './portfolioExport';
import { fmtNum as fmt, pnlColor } from './portfolioUtils';
import { PositionsTable } from './PositionsTable';
import { usePrivacy } from './privacy';
import { PrivacyProvider } from './PrivacyContext';

import { useGetPortfolioSummaryQuery, useLazyGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { PortfolioSummaryDto, PositionDto } from '@/apis/portfolioApis/types';

function PrivacyToggle() {
  const { mode, cycle } = usePrivacy();
  const cfg = {
    full: { icon: <VisibilityIcon />, title: '完整顯示（點擊：只看損益）' },
    changes: { icon: <ShowChartIcon />, title: '只看損益（點擊：隱藏金額）' },
    hidden: { icon: <VisibilityOffIcon />, title: '隱藏金額（點擊：完整顯示）' },
  }[mode];
  return (
    <Tooltip title={cfg.title}>
      <IconButton onClick={cycle} size="small">
        {cfg.icon}
      </IconButton>
    </Tooltip>
  );
}

function ExportMenu({ summary }: { summary: PortfolioSummaryDto }) {
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null);
  const [fetchHistory, { isFetching }] = useLazyGetNetWorthHistoryQuery();

  const close = () => setAnchor(null);

  const exportHistory = async () => {
    close();
    // 180 = the widest range the backend supports; gives the full available series.
    const res = await fetchHistory(180).unwrap();
    exportNetWorthCsv(res.data ?? []);
  };

  return (
    <>
      <Tooltip title="匯出 CSV (Excel)">
        <IconButton onClick={(e) => setAnchor(e.currentTarget)} size="small" disabled={isFetching}>
          <FileDownloadOutlinedIcon />
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={!!anchor} onClose={close}>
        <MenuItem
          onClick={() => {
            close();
            exportPortfolioCsv(summary);
          }}
        >
          目前持倉
        </MenuItem>
        <MenuItem onClick={exportHistory}>淨值歷史 (時間序列)</MenuItem>
      </Menu>
    </>
  );
}

function NetWorthCard({
  total,
  pnl,
  pnlPct,
  hasPnl,
  annualized,
  dayChange,
  dayChangePct,
}: {
  total: number;
  pnl: number;
  pnlPct: number;
  hasPnl: boolean;
  annualized?: number | null;
  dayChange?: number | null;
  dayChangePct?: number | null;
}) {
  const isPositive = pnl >= 0;
  const isDayPositive = (dayChange ?? 0) >= 0;
  const { value, change, showRatio } = usePrivacy();
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
          NT${value(fmt(total))}
        </Typography>
        {dayChange != null && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1 }}>
            <Typography
              variant="body1"
              sx={{ color: pnlColor(dayChange, 'inherit'), fontWeight: 600 }}
              component="span"
            >
              {isDayPositive ? '▲' : '▼'} NT${change(fmt(Math.abs(dayChange)))}
            </Typography>
            {showRatio && dayChangePct != null && (
              <Chip
                label={`${isDayPositive ? '+' : ''}${dayChangePct.toFixed(2)}%`}
                size="small"
                sx={{
                  bgcolor: alpha(isDayPositive ? '#22c55e' : '#ef4444', 0.15),
                  color: isDayPositive ? '#22c55e' : '#ef4444',
                  fontWeight: 700,
                  border: 'none',
                }}
              />
            )}
            <Typography variant="caption" color="text.secondary">
              單日異動 (與前一交易日比)
            </Typography>
          </Box>
        )}
        {hasPnl && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1 }}>
            <Typography
              variant="body1"
              sx={{ color: pnlColor(pnl, 'inherit'), fontWeight: 600 }}
              component="span"
            >
              {isPositive ? '▲' : '▼'} NT${change(fmt(Math.abs(pnl)))}
            </Typography>
            {showRatio && (
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
            )}
            <Typography variant="caption" color="text.secondary">
              未實現損益(已填成本部分)
            </Typography>
          </Box>
        )}
        {hasPnl && annualized != null && showRatio && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 0.5 }}>
            <Chip
              label={`年化 ${annualized >= 0 ? '+' : ''}${annualized.toFixed(2)}%`}
              size="small"
              variant="outlined"
              sx={{
                borderColor: alpha(annualized >= 0 ? '#22c55e' : '#ef4444', 0.5),
                color: annualized >= 0 ? '#22c55e' : '#ef4444',
                fontWeight: 700,
              }}
            />
            <Typography variant="caption" color="text.secondary">
              年化報酬率 (XIRR · 估)
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
  const { value: maskValue, showRatio } = usePrivacy();
  if (value <= 0) return null;
  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          {label}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          NT${maskValue(fmt(value))}
          {showRatio ? ` (${pct.toFixed(1)}%)` : ''}
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
  liability,
  usdTwdRate,
  exposure,
  exposureRatio,
}: {
  twStock: number;
  usStock: number;
  cash: number;
  fund: number;
  liability: number;
  usdTwdRate: number;
  exposure: number;
  exposureRatio: number;
}) {
  const { value, showRatio } = usePrivacy();
  // Percentages are of gross assets (before subtracting debt), not net worth — otherwise
  // liability would shrink the denominator and inflate every other row's percentage.
  const grossAssets = twStock + usStock + cash + fund;
  const pct = (v: number) => (grossAssets > 0 ? (v / grossAssets) * 100 : 0);
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
        {liability > 0 && (
          <Box
            sx={{
              mt: 1.5,
              display: 'flex',
              justifyContent: 'space-between',
            }}
          >
            <Typography variant="body2" sx={{ fontWeight: 600, color: '#ef4444' }}>
              負債
            </Typography>
            <Typography variant="body2" sx={{ color: '#ef4444' }}>
              -NT${value(fmt(liability))}
            </Typography>
          </Box>
        )}
        <Box
          sx={{
            mt: 2,
            pt: 1.5,
            borderTop: '1px dashed',
            borderColor: 'divider',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'baseline',
          }}
        >
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            市場曝險
          </Typography>
          <Typography variant="body2" color="text.secondary">
            NT${value(fmt(exposure))}
            {showRatio ? ` (${exposureRatio.toFixed(1)}%)` : ''}
          </Typography>
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ mt: 0.5, display: 'block' }}>
          正2 ×2、債券/現金/負債 0；可 &gt;100% 表示槓桿
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1.5, display: 'block' }}>
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
            {hasHoldings && summary && <ExportMenu summary={summary} />}
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
                  annualized={summary.annualizedReturnPct}
                  dayChange={summary.dayChangeTWD}
                  dayChangePct={summary.dayChangePct}
                />
              </Grid>
              <Grid size={{ xs: 12, md: 4 }}>
                <AllocationCard
                  exposure={summary.marketExposureTWD}
                  exposureRatio={summary.exposureRatioPct}
                  twStock={summary.twStockTWD}
                  usStock={summary.usStockTWD}
                  cash={summary.cashTWD}
                  fund={summary.fundTWD}
                  liability={summary.liabilityTWD}
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
