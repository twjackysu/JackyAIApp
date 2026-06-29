import CloseIcon from '@mui/icons-material/Close';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import { alpha } from '@mui/material/styles';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';

import { useGetHoldingPriceHistoryQuery } from '@/apis/portfolioApis';
import { PositionDto } from '@/apis/portfolioApis/types';
import { ECHARTS_THEME, ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

interface Props {
  position: PositionDto | null;
  open: boolean;
  onClose: () => void;
}

const fmt = (n: number, decimals = 2) =>
  n.toLocaleString('zh-TW', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });

const pnlColor = (n: number) => (n > 0 ? '#22c55e' : n < 0 ? '#ef4444' : 'text.secondary');

function StatBlock({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <Box sx={{ textAlign: 'center' }}>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {label}
      </Typography>
      <Typography
        variant="body1"
        sx={{ fontWeight: 700, color: color ?? 'text.primary' }}
        component="span"
      >
        {value}
      </Typography>
    </Box>
  );
}

function PriceChart({ market, symbol }: { market: string; symbol: string }) {
  const { data: res, isLoading } = useGetHoldingPriceHistoryQuery({ market, symbol });
  const hist = res?.data;

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress size={28} />
      </Box>
    );
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
            .map((s) => `${s.seriesName}：${hist.currency} ${fmt(Number(s.value))}`),
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
      axisLabel: { formatter: (v: number) => fmt(v, 0) },
      scale: true,
    },
    series,
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="h5" sx={{ fontWeight: 800 }}>
          {hist.currency} {fmt(currentPrice)}
        </Typography>
        {pnlPct != null && (
          <Chip
            icon={
              pnlPct >= 0 ? (
                <TrendingUpIcon fontSize="small" />
              ) : (
                <TrendingDownIcon fontSize="small" />
              )
            }
            label={`${pnlPct >= 0 ? '+' : ''}${fmt(pnlPct)}% vs 成本`}
            size="small"
            sx={{
              bgcolor: alpha(pnlPct >= 0 ? '#22c55e' : '#ef4444', 0.15),
              color: pnlPct >= 0 ? '#22c55e' : '#ef4444',
              fontWeight: 700,
              border: 'none',
            }}
          />
        )}
      </Box>
      <ReactEcharts option={option} theme={ECHARTS_THEME} style={{ height: 260 }} />
    </Box>
  );
}

export function HoldingDetailDrawer({ position, open, onClose }: Props) {
  const hasPnl = position?.unrealizedPnlTWD != null && position?.unrealizedPnlPct != null;

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
                  label={position.market}
                  size="small"
                  color={position.market === 'TW' ? 'primary' : 'warning'}
                  variant="outlined"
                />
              </Box>
              <Typography variant="body2" color="text.secondary">
                {position.name}
              </Typography>
            </Box>
            <Tooltip title="關閉">
              <IconButton onClick={onClose} size="small">
                <CloseIcon />
              </IconButton>
            </Tooltip>
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
            <StatBlock label="持股數量" value={position.quantity.toLocaleString()} />
            <StatBlock label="現價" value={`${position.currency} ${fmt(position.currentPrice)}`} />
            <StatBlock label="市值 (TWD)" value={`NT$${fmt(position.marketValueTWD, 0)}`} />
            {hasPnl ? (
              <>
                <StatBlock
                  label="未實現損益"
                  value={`NT$${fmt(position.unrealizedPnlTWD as number, 0)}`}
                  color={pnlColor(position.unrealizedPnlTWD as number)}
                />
                <StatBlock
                  label="損益%"
                  value={`${(position.unrealizedPnlPct as number) >= 0 ? '+' : ''}${fmt(position.unrealizedPnlPct as number)}%`}
                  color={pnlColor(position.unrealizedPnlPct as number)}
                />
                <StatBlock
                  label="成本基礎"
                  value={`NT$${fmt((position.costBasisTWD as number) ?? 0, 0)}`}
                />
              </>
            ) : (
              <StatBlock label="損益" value="未填成本" />
            )}
          </Box>

          {/* Price chart */}
          <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 1 }}>
            近6個月走勢{hasPnl ? ' · 橘線 = 均攤成本' : ''}
          </Typography>
          <PriceChart market={position.market} symbol={position.symbol} />
        </>
      )}
    </Drawer>
  );
}
