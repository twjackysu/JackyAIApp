import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import CloseIcon from '@mui/icons-material/Close';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import { alpha, useTheme } from '@mui/material/styles';
import Typography from '@mui/material/Typography';

import { usePrivacy } from './privacy';
import { StatBlock } from './StatBlock';
import { valuationExplainer } from './valuationExplainer';

import { useGetHoldingPriceHistoryQuery, useLazyGetAiTechSummaryQuery } from '@/apis/portfolioApis';
import { PositionDto } from '@/apis/portfolioApis/types';
import { ECHARTS_THEME, ECOption } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';
import CenteredSpinner from '@/components/CenteredSpinner';
import { ActionTooltip, InfoPopover } from '@/components/hints';
import MarkdownMessage from '@/components/MarkdownMessage';
import { CREDIT_COSTS } from '@/constants/creditCosts';
import { fmtNum, fmtQty, fmtSigned, GAIN_COLOR, LOSS_COLOR, pnlColor } from '@/utils/format';

interface Props {
  position: PositionDto | null;
  open: boolean;
  onClose: () => void;
}

// Prices/NAV in this drawer default to 2 decimals; share other formatting with the rest of the module.
const fmt = (n: number, decimals = 2) => fmtNum(n, decimals);

function PriceChart({ market, symbol }: { market: string; symbol: string }) {
  const { mode, value, showRatio } = usePrivacy();
  const { data: res, isLoading } = useGetHoldingPriceHistoryQuery({ market, symbol });
  const hist = res?.data;

  if (isLoading) {
    return <CenteredSpinner size={28} sx={{ py: 6 }} />;
  }

  if (!hist || hist.prices.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>
        無歷史價格資料
      </Typography>
    );
  }

  const dates = hist.prices.map((p) => p.date);
  const closes = hist.prices.map((p) => p.close);
  const currentPrice = closes[closes.length - 1] ?? 0;
  const avgCost = hist.avgCost ?? null;
  const pnlPct = avgCost && avgCost > 0 ? ((currentPrice - avgCost) / avgCost) * 100 : null;

  const series: ECOption['series'] = [
    {
      name: '收盤價',
      type: 'line',
      data: closes,
      smooth: false,
      lineStyle: { width: 2, color: '#3B82F6' },
      itemStyle: { color: '#3B82F6' },
      areaStyle: {
        color: {
          type: 'linear',
          x: 0,
          y: 0,
          x2: 0,
          y2: 1,
          colorStops: [
            { offset: 0, color: 'rgba(59,130,246,0.2)' },
            { offset: 1, color: 'rgba(59,130,246,0)' },
          ],
        },
      },
      symbol: 'none',
    },
  ];

  if (avgCost && avgCost > 0) {
    series.push({
      name: '均攤成本',
      type: 'line',
      data: dates.map(() => avgCost),
      lineStyle: { width: 1.5, color: '#F59E0B', type: 'dashed' },
      itemStyle: { color: '#F59E0B' },
      symbol: 'none',
    });
  }

  const option: ECOption = {
    tooltip: {
      trigger: 'axis',
      formatter: (params: unknown) => {
        const p = params as { axisValue: string; seriesName: string; value: number | string }[];
        return [
          `<b>${p[0]?.axisValue ?? ''}</b>`,
          ...p
            .filter((s) => typeof s.value === 'number')
            .map((s) => `${s.seriesName}：${hist.currency} ${value(fmt(Number(s.value)))}`),
        ].join('<br/>');
      },
    },
    legend: { bottom: 0, data: series.map((s) => s.name as string) },
    grid: { top: 16, right: 16, bottom: 48, left: 72 },
    xAxis: {
      type: 'category',
      data: dates,
      boundaryGap: false,
      axisLabel: {
        formatter: (v: string) => v.slice(5),
        interval: Math.floor(dates.length / 6),
      },
    },
    yAxis: {
      type: 'value',
      axisLabel: { formatter: (v: number) => (mode === 'full' ? fmt(v, 0) : '') },
      scale: true,
    },
    series,
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="h5" sx={{ fontWeight: 800 }}>
          {hist.currency} {value(fmt(currentPrice))}
        </Typography>
        {pnlPct != null && showRatio && (
          <Chip
            icon={
              pnlPct >= 0 ? (
                <TrendingUpIcon fontSize="small" />
              ) : (
                <TrendingDownIcon fontSize="small" />
              )
            }
            label={`${fmtSigned(pnlPct, 2)}% vs 成本`}
            size="small"
            sx={{
              bgcolor: alpha(pnlPct >= 0 ? GAIN_COLOR : LOSS_COLOR, 0.15),
              color: pnlPct >= 0 ? GAIN_COLOR : LOSS_COLOR,
              fontWeight: 700,
              border: 'none',
            }}
          />
        )}
      </Box>
      <ReactECharts option={option} theme={ECHARTS_THEME} style={{ height: 260 }} />
    </Box>
  );
}

function AiTechSummary({ market, symbol }: { market: string; symbol: string }) {
  const theme = useTheme();
  const [fetchAi, { data, isFetching, error }] = useLazyGetAiTechSummaryQuery();
  const summary = data?.data;

  return (
    <Box sx={{ mt: 3 }}>
      <Button
        variant="outlined"
        size="small"
        startIcon={<AutoAwesomeIcon />}
        onClick={() => fetchAi({ market, symbol })}
        disabled={isFetching}
        fullWidth
      >
        {isFetching ? 'AI 分析中…' : 'AI 技術面總結'}
      </Button>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
        每次 AI 總結消耗 {CREDIT_COSTS.PORTFOLIO_AI_SUMMARY} 點使用額度
      </Typography>

      {isFetching && <CenteredSpinner size={24} sx={{ py: 3 }} />}

      {error && !isFetching && (
        <Typography color="error" variant="body2" sx={{ mt: 2 }}>
          AI 總結失敗，請稍後再試
        </Typography>
      )}

      {summary && !isFetching && (
        <Box sx={{ mt: 2, p: 2, borderRadius: 2, bgcolor: 'action.hover' }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', mb: 1 }}>
            {summary.trendLabel && (
              <Chip
                label={summary.trendLabel}
                size="small"
                color="primary"
                sx={{ fontWeight: 700 }}
              />
            )}
            {summary.action && (
              <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
                建議：{summary.action}
              </Typography>
            )}
          </Box>
          <MarkdownMessage content={summary.signals} isOwnMessage={false} theme={theme} />
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>
            以上為 AI 依技術面自動生成，僅供參考，非投資建議。
          </Typography>
        </Box>
      )}
    </Box>
  );
}

function FinanceCrossLink({ symbol, market }: { symbol: string; market: string }) {
  return (
    <Button
      variant="text"
      size="small"
      startIcon={<OpenInNewIcon fontSize="small" />}
      href={`/finance?symbol=${encodeURIComponent(symbol)}&market=${encodeURIComponent(market)}`}
      target="_blank"
      rel="noopener noreferrer"
      sx={{ mt: 1 }}
      fullWidth
    >
      在 Finance 查看完整分析
    </Button>
  );
}

export function HoldingDetailDrawer({ position, open, onClose }: Props) {
  const { value, change, showRatio } = usePrivacy();
  const hasPnl = position?.unrealizedPnlTWD != null && position?.unrealizedPnlPct != null;
  const isFund = position?.assetType === 'FUND';
  const explainer = position ? valuationExplainer(position) : null;

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 480 }, p: 3 } } }}
    >
      {position && (
        <>
          {/* Header */}
          <Box
            sx={{
              display: 'flex',
              alignItems: 'flex-start',
              justifyContent: 'space-between',
              mb: 2,
            }}
          >
            <Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Typography variant="h5" sx={{ fontWeight: 800 }}>
                  {position.symbol}
                </Typography>
                <Chip
                  label={isFund ? '基金' : position.market}
                  size="small"
                  color={isFund ? 'info' : position.market === 'TW' ? 'primary' : 'warning'}
                  variant="outlined"
                />
              </Box>
              <Typography variant="body2" color="text.secondary">
                {position.name}
              </Typography>
            </Box>
            <ActionTooltip title="關閉">
              <IconButton onClick={onClose} size="small">
                <CloseIcon />
              </IconButton>
            </ActionTooltip>
          </Box>

          <Divider sx={{ mb: 2 }} />

          {/* Stats row */}
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: 'repeat(3, 1fr)',
              gap: 1,
              mb: 3,
              p: 2,
              borderRadius: 2,
              bgcolor: 'action.hover',
            }}
          >
            {/* Masked like every other absolute value: current price is public knowledge for
                any listed security, so a leaked quantity alone still lets a screenshot viewer
                compute market value themselves. */}
            <StatBlock
              label={isFund ? '單位數' : '持股數量'}
              value={value(fmtQty(position.quantity))}
              align="center"
            />
            <StatBlock
              label={isFund ? '淨值' : '現價'}
              value={`${position.currency} ${value(fmt(position.currentPrice))}`}
              align="center"
            />
            <StatBlock
              label="市值 (TWD)"
              value={`NT$${value(fmt(position.marketValueTWD, 0))}`}
              align="center"
            />
            {hasPnl ? (
              <>
                <StatBlock
                  label="未實現損益"
                  value={`NT$${change(fmt(position.unrealizedPnlTWD as number, 0))}`}
                  color={pnlColor(position.unrealizedPnlTWD as number)}
                  align="center"
                />
                {showRatio && (
                  <StatBlock
                    label="損益%"
                    value={`${fmtSigned(position.unrealizedPnlPct as number, 2)}%`}
                    color={pnlColor(position.unrealizedPnlPct as number)}
                    align="center"
                  />
                )}
                <StatBlock
                  label="成本基礎"
                  value={`NT$${value(fmt((position.costBasisTWD as number) ?? 0, 0))}`}
                  align="center"
                />
              </>
            ) : (
              <StatBlock label="損益" value="未填成本" align="center" />
            )}
            {position.fxRateUsed != null && (
              <Box>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.25 }}>
                  <Typography variant="caption" color="text.secondary">
                    {position.fxRateSource === 'TDCC'
                      ? '匯率 (集保)'
                      : position.fxRateSource === 'CATHAY'
                        ? '匯率 (國泰世華)'
                        : '匯率 (中間價)'}
                  </Typography>
                  {explainer && (
                    <InfoPopover content={explainer} ariaLabel="市值與損益計算方式" iconSize={14} />
                  )}
                </Box>
                <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                  {fmt(position.fxRateUsed, 4)}
                </Typography>
              </Box>
            )}
          </Box>

          {/* Price chart */}
          <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 1 }}>
            {isFund ? '近一年淨值走勢' : '近6個月走勢'}
            {hasPnl ? ' · 橘線 = 均攤成本' : ''}
          </Typography>
          <PriceChart market={isFund ? 'FUND' : position.market} symbol={position.symbol} />

          <AiTechSummary market={isFund ? 'FUND' : position.market} symbol={position.symbol} />
          {position.assetType === 'STOCK' && (
            <FinanceCrossLink symbol={position.symbol} market={position.market} />
          )}
        </>
      )}
    </Drawer>
  );
}
