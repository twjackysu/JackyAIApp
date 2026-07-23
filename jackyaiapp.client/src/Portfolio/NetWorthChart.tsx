import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonGroup from '@mui/material/ButtonGroup';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';
import { useState } from 'react';

import { MASK, usePrivacy } from './privacy';

import { useGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { NetWorthGroupBy } from '@/apis/portfolioApis/types';
import { ECHARTS_THEME, ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

const RANGES: { label: string; days: number }[] = [
  { label: '1M', days: 30 },
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
];

const VIEW_BY_OPTIONS: { label: string; value: NetWorthGroupBy }[] = [
  { label: '資產類型', value: 'assetType' },
  { label: '產業', value: 'category' },
  { label: '子類別', value: 'subCategory' },
];

// One combined map for every key any groupBy mode can produce — assetType and category modes
// both use "Cash"/"Liability", and sharing one entry keeps that color/label consistent across
// modes. A key not listed here falls back to its raw key as the label and a neutral gray — for
// assetType/category/currency that "shouldn't happen" (closed backend enums), but subCategory is
// deliberately open-ended free-form AI output, so most of its series are expected to land in that
// fallback gray rather than get a bespoke color.
const KEY_LABELS: Record<string, string> = {
  // assetType
  TwStock: '台股',
  UsStock: '美股',
  Cash: '現金',
  Fund: '基金',
  Liability: '負債',
  // category (PortfolioCategory enum names)
  Semiconductor: '半導體',
  TechSoftware: '科技軟體',
  Financials: '金融',
  Manufacturing: '傳產製造',
  Consumer: '民生消費',
  Healthcare: '醫療生技',
  Materials: '能源原物料',
  Utilities: '電信公用',
  RealEstate: '房地產REITs',
  BroadMarket: '市值型',
  DividendEquity: '高股息',
  Bond: '債券',
  Other: '其他',
  // currency (button removed, but the backend groupBy mode still exists — keep the labels)
  TWD: '台幣',
  USD: '美元',
  // subCategory: 現金/負債/未分類 are already the raw key text (no translation needed); every
  // other key is a free-form AI-generated label and passes through as-is via the `?? key` fallback.
};

const KEY_COLORS: Record<string, string> = {
  TwStock: '#3B82F6',
  UsStock: '#10B981',
  Cash: '#A78BFA',
  Fund: '#EC4899',
  Liability: '#ef4444',
  Semiconductor: '#3B82F6',
  TechSoftware: '#6366F1',
  Financials: '#F59E0B',
  Manufacturing: '#78716C',
  Consumer: '#F97316',
  Healthcare: '#14B8A6',
  Materials: '#84CC16',
  Utilities: '#0EA5E9',
  RealEstate: '#D946EF',
  BroadMarket: '#10B981',
  DividendEquity: '#EAB308',
  Bond: '#8B5CF6',
  Other: '#9CA3AF',
  TWD: '#3B82F6',
  USD: '#10B981',
  // subCategory's fixed keys — reuse the same colors as their assetType-mode counterparts.
  現金: '#A78BFA',
  負債: '#ef4444',
  未分類: '#9CA3AF',
};

const fmt = (n: number) => {
  // Negating a zero liability produces JS's negative zero, which toLocaleString renders
  // as the string "-0" — normalize it away first (n === 0 is true for both +0 and -0).
  const v = n === 0 ? 0 : n;
  // Compare by magnitude, not value — some groups (e.g. Liability) are negative, and a debt
  // over 1M should still get the "-X.XXM" treatment instead of a raw number.
  return Math.abs(v) >= 1_000_000
    ? `${(v / 1_000_000).toFixed(2)}M`
    : v.toLocaleString('zh-TW', { maximumFractionDigits: 0 });
};

export function NetWorthChart() {
  const [days, setDays] = useState(90);
  const [groupBy, setGroupBy] = useState<NetWorthGroupBy>('assetType');
  const { mode } = usePrivacy();
  const { data: res, isLoading } = useGetNetWorthHistoryQuery({ days, groupBy });
  const points = res?.data ?? [];

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (points.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        資料不足，更新持倉後即可看到圖表
      </Typography>
    );
  }

  const dates = points.map((p) => p.date);
  const totals = points.map((p) => p.totalTWD);

  // Every distinct group key seen across the whole range — different days can have different
  // active groups (e.g. a category only appears once its symbol gets held/classified).
  const allKeys = Array.from(new Set(points.flatMap((p) => p.groups.map((g) => g.key))));
  const breakdown = allKeys
    .map((key) => ({
      key,
      name: KEY_LABELS[key] ?? key,
      color: KEY_COLORS[key] ?? '#9CA3AF',
      values: points.map((p) => p.groups.find((g) => g.key === key)?.valueTWD ?? 0),
    }))
    .filter((s) => s.values.some((v) => v !== 0));

  const option: ECOption = {
    tooltip: {
      trigger: 'axis',
      formatter: (params: unknown) => {
        const p = params as {
          axisValue: string;
          seriesName: string;
          value: number;
          dataIndex: number;
        }[];
        const date = p[0]?.axisValue ?? '';
        const dayTotal = totals[p[0]?.dataIndex ?? 0] || 0;
        const line = (s: { seriesName: string; value: number }) => {
          if (mode === 'full') return `${s.seriesName}：NT$${fmt(s.value)}`;
          // 'changes' hides both amounts and ratios; 'hidden' shows share-of-total instead.
          if (mode === 'changes') return `${s.seriesName}：${MASK}`;
          const pct = dayTotal > 0 ? (s.value / dayTotal) * 100 : 0;
          return `${s.seriesName}：${pct.toFixed(1)}%`;
        };
        // The stack already sums to the total, so no separate "總資產" series is rendered —
        // just surface the number (or masked value) in the tooltip for a quick read.
        const totalLine =
          mode === 'full'
            ? `<b>總資產：NT$${fmt(dayTotal)}</b>`
            : mode === 'changes'
              ? `<b>總資產：${MASK}</b>`
              : null;
        return [`<b>${date}</b>`, totalLine, ...p.map(line)].filter(Boolean).join('<br/>');
      },
    },
    legend: { bottom: 0, data: breakdown.map((s) => s.name) },
    grid: { top: 16, right: 16, bottom: 48, left: mode === 'full' ? 70 : 24 },
    xAxis: { type: 'category', data: dates, boundaryGap: false },
    yAxis: {
      type: 'value',
      // Mask the scale numbers unless fully visible; stack height/proportions stay accurate.
      axisLabel: { formatter: (v: number) => (mode === 'full' ? `${fmt(v)}` : '') },
    },
    series: breakdown.map((s) => ({
      name: s.name,
      type: 'line' as const,
      // Any group that ever goes negative (Liability under assetType/category grouping — or in
      // principle any group under currency grouping) gets its own stack, not the shared 'total'
      // one: ECharts stacks cumulatively, so a negative series sharing a stack with positive ones
      // renders as "gross assets, then a jump down" (its cumulative value = everything below it
      // + itself) instead of its own value dipping cleanly from 0. Its own stack group renders it
      // independently, from a 0 baseline down to its actual (negative) value.
      stack: s.values.some((v) => v < 0) ? `neg-${s.key}` : 'total',
      data: s.values,
      smooth: true,
      lineStyle: { width: 1.5, color: s.color },
      itemStyle: { color: s.color },
      areaStyle: { color: s.color, opacity: 0.65 },
      symbol: 'none' as const,
    })),
  };

  return (
    <Box>
      <Box
        sx={{
          display: 'flex',
          flexWrap: 'wrap',
          justifyContent: 'space-between',
          alignItems: 'center',
          gap: 1,
          mb: 1,
        }}
      >
        <ButtonGroup size="small" variant="outlined">
          {VIEW_BY_OPTIONS.map((v) => (
            <Button
              key={v.value}
              onClick={() => setGroupBy(v.value)}
              variant={groupBy === v.value ? 'contained' : 'outlined'}
            >
              {v.label}
            </Button>
          ))}
        </ButtonGroup>
        <ButtonGroup size="small" variant="outlined">
          {RANGES.map((r) => (
            <Button
              key={r.days}
              onClick={() => setDays(r.days)}
              variant={days === r.days ? 'contained' : 'outlined'}
            >
              {r.label}
            </Button>
          ))}
        </ButtonGroup>
      </Box>
      <ReactEcharts option={option} theme={ECHARTS_THEME} style={{ height: 280 }} />
    </Box>
  );
}
