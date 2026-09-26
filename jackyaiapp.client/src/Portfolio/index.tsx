import FileDownloadOutlinedIcon from '@mui/icons-material/FileDownloadOutlined';
import HelpOutlineIcon from '@mui/icons-material/HelpOutlineOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import IconButton from '@mui/material/IconButton';
import LinearProgress from '@mui/material/LinearProgress';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { ReactNode, useState } from 'react';

import { BenchmarkChart } from './BenchmarkChart';
import { ChartSectionCard } from './ChartSectionCard';
import { ConcentrationCard } from './ConcentrationCard';
import { ContributionWaterfallChart } from './ContributionWaterfallChart';
import { DrawdownChart } from './DrawdownChart';
import { drawdownBufferColor, EXPOSURE_TOOLTIP } from './exposureGauges';
import { loadFeeDiscountRate, saveFeeDiscountRate } from './feeDiscountRate';
import { FeeSettingsDialog } from './FeeSettingsDialog';
import { FinancialChangesChart } from './FinancialChangesChart';
import { HoldingDetailDrawer } from './HoldingDetailDrawer';
import { HoldingsEditor } from './HoldingsEditor';
import { NetWorthChart } from './NetWorthChart';
import { NetWorthTrendChart } from './NetWorthTrendChart';
import { usePortfolioTour } from './onboardingTour';
import { exportNetWorthCsv, exportPortfolioCsv } from './portfolioExport';
import { PositionsTable } from './PositionsTable';
import { usePrivacy } from './privacy';
import { PrivacyProvider } from './PrivacyContext';
import { RebalancingCard } from './RebalancingCard';

import { useGetPortfolioSummaryQuery, useLazyGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { PortfolioSummaryDto, PositionDto } from '@/apis/portfolioApis/types';
import CenteredSpinner from '@/components/CenteredSpinner';
import { ActionTooltip, InfoPopover } from '@/components/hints';
import { fmtNum as fmt, fmtSignedPct, GAIN_COLOR, LOSS_COLOR, pnlColor } from '@/utils/format';

function PrivacyToggle() {
  const { mode, cycle } = usePrivacy();
  const cfg = {
    full: { icon: <VisibilityIcon />, title: '完整顯示（點擊：只看損益）' },
    changes: { icon: <ShowChartIcon />, title: '只看損益（點擊：隱藏金額）' },
    hidden: { icon: <VisibilityOffIcon />, title: '隱藏金額（點擊：完整顯示）' },
  }[mode];
  return (
    <ActionTooltip title={cfg.title}>
      <IconButton onClick={cycle} size="small" aria-label={cfg.title}>
        {cfg.icon}
      </IconButton>
    </ActionTooltip>
  );
}

function ExportMenu({ summary }: { summary: PortfolioSummaryDto }) {
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null);
  const [fetchHistory, { isFetching }] = useLazyGetNetWorthHistoryQuery();

  const close = () => setAnchor(null);

  const exportHistory = async () => {
    close();
    // 180 = the widest range the backend supports; gives the full available series.
    // groupBy defaults to assetType server-side — CSV export wants the classic 5-column breakdown.
    const res = await fetchHistory({ days: 180 }).unwrap();
    exportNetWorthCsv(res.data ?? []);
  };

  return (
    <>
      <ActionTooltip title="匯出 CSV (Excel)">
        <IconButton onClick={(e) => setAnchor(e.currentTarget)} size="small" disabled={isFetching}>
          <FileDownloadOutlinedIcon />
        </IconButton>
      </ActionTooltip>
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

/** The caption trailing each P&L row wraps onto its own line on phones, where the amount and the
 * percentage badge already fill the row. */
const PNL_CAPTION_SX = {
  flexBasis: { xs: '100%', sm: 'auto' },
  fontSize: { xs: '0.7rem', sm: '0.75rem' },
} as const;

/** Signed-percentage badge. Zero counts as a gain here (matching the ▲ threshold of the row it
 * sits in), so it never renders in the loss colour without a "-" in the label. */
function PctChip({
  pct,
  label = '',
  outlined = false,
}: {
  pct: number;
  label?: string;
  outlined?: boolean;
}) {
  const color = pct >= 0 ? GAIN_COLOR : LOSS_COLOR;
  return (
    <Chip
      label={`${label}${fmtSignedPct(pct)}`}
      size="small"
      variant={outlined ? 'outlined' : 'filled'}
      sx={
        outlined
          ? { borderColor: alpha(color, 0.5), color, fontWeight: 700 }
          : { bgcolor: alpha(color, 0.15), color, fontWeight: 700, border: 'none' }
      }
    />
  );
}

/**
 * One "▲ NT$x,xxx  (+y.yy%)  <caption>" line of the net-worth card — 單日異動, 未實現損益 and
 * 含息損益 are the same row with different numbers behind them. The amount honours the privacy
 * mode's `change` mask; the badge is dropped whenever ratios are hidden or `pct` is unknown.
 */
function PnlRow({
  amount,
  pct,
  caption,
}: {
  amount: number;
  pct?: number | null;
  caption: ReactNode;
}) {
  const { change, showRatio } = usePrivacy();
  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1, mt: 1 }}>
      <Typography
        variant="body1"
        sx={{ color: pnlColor(amount, 'inherit'), fontWeight: 600 }}
        component="span"
      >
        {amount >= 0 ? '▲' : '▼'} NT${change(fmt(Math.abs(amount)))}
      </Typography>
      {showRatio && pct != null && <PctChip pct={pct} />}
      <Typography variant="caption" color="text.secondary" sx={PNL_CAPTION_SX}>
        {caption}
      </Typography>
    </Box>
  );
}

function NetWorthCard({
  total,
  realEstate,
  liquidGrossAssets,
  pnl,
  pnlPct,
  hasPnl,
  estimatedSellCost,
  totalDividends,
  totalReturnPnl,
  totalReturnPct,
  annualized,
  dayChange,
  dayChangePct,
}: {
  total: number;
  realEstate: number;
  liquidGrossAssets: number;
  pnl: number;
  pnlPct: number;
  hasPnl: boolean;
  estimatedSellCost: number;
  totalDividends: number;
  totalReturnPnl: number;
  totalReturnPct: number;
  annualized?: number | null;
  dayChange?: number | null;
  dayChangePct?: number | null;
}) {
  const { value, change, showRatio } = usePrivacy();
  const liquidNetWorth = total - realEstate;
  return (
    <Card
      data-tour="portfolio-tour-networth"
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
      <CardContent sx={{ p: { xs: 2, sm: 3 } }}>
        <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 2 }}>
          流動資產
        </Typography>
        {/* Headline is gross liquid assets (excl. real estate, not net of liability), not total
            net worth: 單日異動/未實現/含息損益 % below are already computed against this same
            figure (see PortfolioService.LiquidGrossAssetsTWD), and a self-occupied home that's
            never getting sold — plus the debt against it — doesn't belong in "what can I actually
            deploy" the way it does in a legal/accounting net-worth figure. Responsive font size
            instead of a fixed h3 — at the theme's default h3 size, a 8-digit TWD figure ran wider
            than a phone screen and got clipped. */}
        <Typography
          sx={{
            fontWeight: 800,
            mt: 0.5,
            letterSpacing: -1,
            fontSize: { xs: '2rem', sm: '2.5rem', md: '3rem' },
            wordBreak: 'break-all',
          }}
        >
          NT${value(fmt(liquidGrossAssets))}
        </Typography>
        {/* Only worth surfacing once they diverge from the headline — otherwise these would just
            repeat the same number. */}
        {total !== liquidGrossAssets && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            總資產淨值 NT${value(fmt(total))}
          </Typography>
        )}
        {liquidNetWorth !== liquidGrossAssets && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            流動資產淨值 NT${value(fmt(liquidNetWorth))}
          </Typography>
        )}
        {dayChange != null && (
          <PnlRow amount={dayChange} pct={dayChangePct} caption="單日異動 (與前一交易日比)" />
        )}
        {hasPnl && (
          <PnlRow
            amount={pnl}
            pct={pnlPct}
            caption={
              <>
                未實現損益(已填成本部分)
                {estimatedSellCost > 0
                  ? `，已扣估計台股賣出費用 NT$${change(fmt(estimatedSellCost))}`
                  : ''}
              </>
            }
          />
        )}
        {hasPnl && totalDividends !== 0 && (
          <PnlRow
            amount={totalReturnPnl}
            pct={totalReturnPct}
            caption={`含息損益(含累計股息 NT$${change(fmt(totalDividends))}，稅前)`}
          />
        )}
        {hasPnl && annualized != null && showRatio && (
          <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1, mt: 0.5 }}>
            <PctChip pct={annualized} label="年化 " outlined />
            <Typography variant="caption" color="text.secondary" sx={PNL_CAPTION_SX}>
              年化報酬率 (XIRR·含息·估)
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
  realEstate,
  liability,
  usdTwdRate,
  exposure,
  exposureOfLiquidAssets,
  leverageMultiple,
  drawdownBuffer,
}: {
  twStock: number;
  usStock: number;
  cash: number;
  fund: number;
  realEstate: number;
  liability: number;
  usdTwdRate: number;
  exposure: number;
  exposureOfLiquidAssets: number | null;
  leverageMultiple: number | null;
  drawdownBuffer: number | null;
}) {
  const { value, showRatio } = usePrivacy();
  // Percentages are of gross assets (before subtracting debt), not net worth — otherwise
  // liability would shrink the denominator and inflate every other row's percentage. Real estate
  // is included: it's a real gross asset, just zero market exposure (see the caption below).
  const grossAssets = twStock + usStock + cash + fund + realEstate;
  const pct = (v: number) => (grossAssets > 0 ? (v / grossAssets) * 100 : 0);
  return (
    <Card
      data-tour="portfolio-tour-allocation"
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
          {realEstate > 0 && (
            <AllocationRow
              label="不動產"
              value={realEstate}
              pct={pct(realEstate)}
              color="#F59E0B"
            />
          )}
        </Box>
        {liability > 0 && (
          <Box
            sx={{
              mt: 1.5,
              display: 'flex',
              justifyContent: 'space-between',
            }}
          >
            <Typography variant="body2" sx={{ fontWeight: 600, color: LOSS_COLOR }}>
              負債
            </Typography>
            <Typography variant="body2" sx={{ color: LOSS_COLOR }}>
              -NT${value(fmt(liability))}
              {showRatio ? ` (-${pct(liability).toFixed(1)}%)` : ''}
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
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              市場曝險
            </Typography>
            {/* All three gauges below share this one numerator and differ only by denominator —
                the formulas live behind this button rather than inline, since spelling them out
                would leave more explanation than numbers on a card this size. */}
            <InfoPopover
              content={EXPOSURE_TOOLTIP}
              ariaLabel="市場曝險說明"
              iconSize={14}
              data-tour="portfolio-tour-exposure-help"
            />
          </Box>
          <Typography variant="body2" color="text.secondary">
            NT${value(fmt(exposure))}
          </Typography>
        </Box>
        {showRatio && (
          <Box
            sx={{
              display: 'flex',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
              gap: 1,
              mt: 0.75,
            }}
          >
            {/* 水位 divides by gross assets, 槓桿 by net — the same numerator over two different
                denominators, so they're given different units (% vs ×) to stop them being read as
                the same kind of number sitting side by side. */}
            {exposureOfLiquidAssets != null && (
              <Typography variant="caption" color="text.secondary">
                水位{' '}
                <Box component="span" sx={{ fontWeight: 700, color: 'text.primary' }}>
                  {exposureOfLiquidAssets.toFixed(1)}%
                </Box>
              </Typography>
            )}
            <Typography variant="caption" color="text.secondary">
              槓桿{' '}
              <Box
                component="span"
                sx={{
                  fontWeight: 700,
                  // Null exactly at insolvency, where exposure ÷ equity explodes and then flips to
                  // a negative that misleadingly reads as *safer* than a large positive one.
                  color: leverageMultiple == null ? LOSS_COLOR : 'text.primary',
                }}
              >
                {leverageMultiple != null ? `${leverageMultiple.toFixed(2)}×` : '資不抵債'}
              </Box>
            </Typography>
          </Box>
        )}
        {/* The leverage multiple's reciprocal, kept alongside it because it stays monotonic and
            continuous straight through insolvency where the multiple can't. Phrased as a drawdown
            the user can feel (6% is a bad day, 20% a correction) rather than a bare ratio. */}
        {showRatio && drawdownBuffer != null && (
          <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 0.5 }}>
            <Typography variant="caption" color="text.secondary">
              抗跌空間
            </Typography>
            <Typography
              variant="caption"
              sx={{ fontWeight: 700, color: drawdownBufferColor(drawdownBuffer) }}
            >
              {drawdownBuffer > 0 ? `再跌 ${drawdownBuffer.toFixed(1)}% 歸零` : '淨值已為負'}
            </Typography>
          </Box>
        )}
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1.5, display: 'block' }}>
          USD/TWD = {usdTwdRate.toFixed(2)}
        </Typography>
      </CardContent>
    </Card>
  );
}

export default function Portfolio() {
  const [feeDiscountRate, setFeeDiscountRate] = useState(loadFeeDiscountRate);
  const {
    data: summaryRes,
    isLoading,
    isFetching,
    isError,
  } = useGetPortfolioSummaryQuery(feeDiscountRate);
  const summary = summaryRes?.data;
  // Background refetch (e.g. right after saving holdings — the summary recomputes prices/
  // dividends/FX live, which can take a few seconds) with no dedicated spinner otherwise: the
  // page kept showing the pre-save numbers with zero feedback that anything was happening.
  const isRefetching = isFetching && !isLoading;
  const [selected, setSelected] = useState<PositionDto | null>(null);

  const handleSaveFeeDiscountRate = (rate: number) => {
    saveFeeDiscountRate(rate);
    setFeeDiscountRate(rate); // triggers a refetch with the new rate — RTK Query caches by arg
  };

  const hasHoldings = (summary?.positions?.length ?? 0) > 0;
  const hasPnl = (summary?.costBasisTWD ?? 0) > 0;
  // Auto-launches once per browser the first time all tour target elements exist — see
  // onboardingTour.ts. hasHoldings picks the variant: empty-state intro (where to start) for a
  // brand-new user, full dashboard walkthrough once holdings exist.
  const { startTour } = usePortfolioTour(!isLoading && !isError && !!summary, hasHoldings);

  return (
    <PrivacyProvider>
      <Box sx={{ maxWidth: 1100, mx: 'auto', py: 3 }}>
        {/* Thin top-of-page progress bar for background refetches (e.g. right after saving
            holdings) — the summary recomputes live prices/dividends/FX and can take a few
            seconds, so without this the page just sat on stale numbers with no feedback. */}
        {isRefetching && <LinearProgress sx={{ mb: 2 }} />}
        {/* Header — stacked on mobile instead of one row: squeezing the title next to 5 action
            controls was compressing the "更新紀錄"/"更新持倉" buttons enough to wrap their own
            label text mid-word. */}
        <Box
          sx={{
            display: 'flex',
            flexDirection: { xs: 'column', sm: 'row' },
            alignItems: { xs: 'stretch', sm: 'center' },
            justifyContent: 'space-between',
            gap: { xs: 1.5, sm: 0 },
            mb: 3,
          }}
        >
          <Box>
            <Typography variant="h5" sx={{ fontWeight: 700 }}>
              My Portfolio
            </Typography>
            <Typography variant="body2" color="text.secondary">
              台股 · 美股 · 現金 · 基金 統一以台幣淨值追蹤
              {summary?.effectiveDate ? ` · 持倉生效日 ${summary.effectiveDate}` : ''}
              {isRefetching ? ' · 更新中…' : ''}
            </Typography>
          </Box>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
            {hasHoldings && summary && <ExportMenu summary={summary} />}
            <FeeSettingsDialog
              feeDiscountRate={feeDiscountRate}
              onSave={handleSaveFeeDiscountRate}
            />
            <ActionTooltip title="重新看導覽教學">
              <IconButton onClick={startTour} size="small" aria-label="重新看導覽教學">
                <HelpOutlineIcon />
              </IconButton>
            </ActionTooltip>
            <PrivacyToggle />
            <HoldingsEditor />
          </Box>
        </Box>

        {isLoading && <CenteredSpinner sx={{ py: 8 }} />}

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
                  realEstate={summary.realEstateTWD}
                  liquidGrossAssets={summary.liquidGrossAssetsTWD}
                  pnl={summary.unrealizedPnlTWD}
                  pnlPct={summary.unrealizedPnlPct}
                  hasPnl={hasPnl}
                  estimatedSellCost={summary.estimatedSellCostTWD}
                  totalDividends={summary.totalDividendsTWD}
                  totalReturnPnl={summary.totalReturnPnlTWD}
                  totalReturnPct={summary.totalReturnPct}
                  annualized={summary.annualizedReturnPct}
                  dayChange={summary.dayChangeTWD}
                  dayChangePct={summary.dayChangePct}
                />
              </Grid>
              <Grid size={{ xs: 12, md: 4 }}>
                <AllocationCard
                  exposure={summary.marketExposureTWD}
                  exposureOfLiquidAssets={summary.exposureOfLiquidAssetsPct}
                  leverageMultiple={summary.liquidExposureMultiple}
                  drawdownBuffer={summary.drawdownBufferPct}
                  twStock={summary.twStockTWD}
                  usStock={summary.usStockTWD}
                  cash={summary.cashTWD}
                  fund={summary.fundTWD}
                  realEstate={summary.realEstateTWD}
                  liability={summary.liabilityTWD}
                  usdTwdRate={summary.usdTwdRate}
                />
              </Grid>
            </Grid>

            {/* Three charts answering three different questions, deliberately not merged:
                trend ("better or worse off?"), composition ("what am I holding?"), and change
                ("how much moved this period?"). The composition chart alone used to carry all
                three jobs, which is why a 20k move against 10M of assets was invisible on it. */}
            <ChartSectionCard title="淨值走勢">
              <NetWorthTrendChart />
            </ChartSectionCard>

            <ChartSectionCard title="資產與負債組成" data-tour="portfolio-tour-chart">
              <NetWorthChart />
            </ChartSectionCard>

            <ChartSectionCard title="淨值變化量">
              <FinancialChangesChart />
            </ChartSectionCard>

            <ChartSectionCard title="大盤對照">
              <BenchmarkChart />
            </ChartSectionCard>

            <ChartSectionCard title="報酬貢獻度">
              <ContributionWaterfallChart positions={summary.positions} />
            </ChartSectionCard>

            <ChartSectionCard title="回撤分析">
              <DrawdownChart />
            </ChartSectionCard>

            <ChartSectionCard title="持股集中度">
              <ConcentrationCard positions={summary.positions} />
            </ChartSectionCard>

            <ChartSectionCard title="再平衡建議">
              <RebalancingCard summary={summary} />
            </ChartSectionCard>

            <Card
              data-tour="portfolio-tour-positions"
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
          <Box data-tour="portfolio-tour-empty" sx={{ textAlign: 'center', py: 8 }}>
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
