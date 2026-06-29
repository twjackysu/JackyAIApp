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
import { HoldingDto } from '@/apis/portfolioApis/types';
import { ECHARTS_THEME, ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

interface Props {
  holding: HoldingDto | null;
  open: boolean;
  onClose: () => void;
}

const fmt = (n: number, decimals = 2) =>
  n.toLocaleString('zh-TW', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });

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

function PriceChart({
  market,
  symbol,
  avgCost,
}: {
  market: string;
  symbol: string;
  avgCost: number;
}) {
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
  const pnlPct = avgCost > 0 ? ((currentPrice - avgCost) / avgCost) * 100 : 0;

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
    legend: { bottom: 0, data: ['收盤價', '均攤成本'] },
    grid: { top: 16, right: 16, bottom: 48, left: 72 },
    xAxis: {
      type: 'category',
      data: dates,
      boundaryGap: false,
      axisLabel: {
        formatter: (v: string) => v.slice(5), // show MM-DD only
        interval: Math.floor(dates.length / 6),
      },
    },
    yAxis: {
      type: 'value',
      axisLabel: { formatter: (v: number) => fmt(v, 0) },
      scale: true,
    },
    series: [
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
      {
        name: '均攤成本',
        type: 'line',
        data: dates.map(() => avgCost),
        lineStyle: { width: 1.5, color: '#F59E0B', type: 'dashed' },
        itemStyle: { color: '#F59E0B' },
        symbol: 'none',
      },
    ],
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="h5" sx={{ fontWeight: 800 }}>
          {hist.currency} {fmt(currentPrice)}
        </Typography>
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
      </Box>
      <ReactEcharts option={option} theme={ECHARTS_THEME} style={{ height: 260 }} />
    </Box>
  );
}

export function HoldingDetailDrawer({ holding, open, onClose }: Props) {
  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 480 }, p: 3 } } }}
    >
      {holding && (
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
                  {holding.symbol}
                </Typography>
                <Chip
                  label={holding.market}
                  size="small"
                  color={holding.market === 'TW' ? 'primary' : 'warning'}
                  variant="outlined"
                />
              </Box>
              <Typography variant="body2" color="text.secondary">
                {holding.companyName}
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
            <StatBlock label="持股數量" value={holding.quantity.toLocaleString()} />
            <StatBlock label="均攤成本" value={`${holding.currency} ${fmt(holding.avgCost)}`} />
            <StatBlock label="市值 (TWD)" value={`NT$${fmt(holding.marketValueTWD, 0)}`} />
            <StatBlock
              label="未實現損益"
              value={`NT$${fmt(holding.unrealizedPnlTWD, 0)}`}
              color={pnlColor(holding.unrealizedPnlTWD)}
            />
            <StatBlock
              label="損益%"
              value={`${holding.unrealizedPnlPct >= 0 ? '+' : ''}${fmt(holding.unrealizedPnlPct)}%`}
              color={pnlColor(holding.unrealizedPnlPct)}
            />
            <StatBlock label="成本基礎" value={`NT$${fmt(holding.costBasisTWD, 0)}`} />
          </Box>

          {/* Price chart */}
          <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 1 }}>
            近6個月走勢 · 橘線 = 均攤成本
          </Typography>
          <PriceChart market={holding.market} symbol={holding.symbol} avgCost={holding.avgCost} />
        </>
      )}
    </Drawer>
  );
}
