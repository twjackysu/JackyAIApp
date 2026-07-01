import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonGroup from '@mui/material/ButtonGroup';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';
import { useState } from 'react';

import { MASK, usePrivacy } from './privacy';

import { useGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME, ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

const RANGES: { label: string; days: number }[] = [
  { label: '1M', days: 30 },
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
];

const fmt = (n: number) =>
  n >= 1_000_000
    ? `${(n / 1_000_000).toFixed(2)}M`
    : n.toLocaleString('zh-TW', { maximumFractionDigits: 0 });

export function NetWorthChart() {
  const [days, setDays] = useState(90);
  const { mode } = usePrivacy();
  const { data: res, isLoading } = useGetNetWorthHistoryQuery(days);
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
  const twValues = points.map((p) => p.twStockTWD);
  const usValues = points.map((p) => p.usStockTWD);
  const cashValues = points.map((p) => p.cashTWD);
  const fundValues = points.map((p) => p.fundTWD);

  const breakdown = [
    { name: '台股', values: twValues, color: '#3B82F6' },
    { name: '美股', values: usValues, color: '#10B981' },
    { name: '現金', values: cashValues, color: '#A78BFA' },
    { name: '基金', values: fundValues, color: '#EC4899' },
  ].filter((s) => s.values.some((v) => v > 0));

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
        const line = (s: { seriesName: string; value: number; dataIndex: number }) => {
          if (mode === 'full') return `${s.seriesName}：NT$${fmt(s.value)}`;
          // 'changes' hides both amounts and ratios; 'hidden' shows share-of-total instead.
          if (mode === 'changes') return `${s.seriesName}：${MASK}`;
          const dayTotal = totals[s.dataIndex] || 0;
          const pct = dayTotal > 0 ? (s.value / dayTotal) * 100 : 0;
          return `${s.seriesName}：${pct.toFixed(1)}%`;
        };
        return [`<b>${date}</b>`, ...p.map(line)].join('<br/>');
      },
    },
    legend: { bottom: 0, data: ['總資產', ...breakdown.map((s) => s.name)] },
    grid: { top: 16, right: 16, bottom: 48, left: mode === 'full' ? 70 : 24 },
    xAxis: { type: 'category', data: dates, boundaryGap: false },
    yAxis: {
      type: 'value',
      // Mask the scale numbers unless fully visible; line heights/proportions stay accurate.
      axisLabel: { formatter: (v: number) => (mode === 'full' ? `${fmt(v)}` : '') },
    },
    series: [
      {
        name: '總資產',
        type: 'line',
        data: totals,
        smooth: true,
        lineStyle: { width: 2.5, color: '#F59E0B' },
        itemStyle: { color: '#F59E0B' },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0,
            y: 0,
            x2: 0,
            y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(245,158,11,0.25)' },
              { offset: 1, color: 'rgba(245,158,11,0)' },
            ],
          },
        },
        symbol: 'none',
      },
      ...breakdown.map((s) => ({
        name: s.name,
        type: 'line' as const,
        data: s.values,
        smooth: true,
        lineStyle: { width: 1.5, color: s.color, type: 'dashed' as const },
        itemStyle: { color: s.color },
        symbol: 'none' as const,
      })),
    ],
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1 }}>
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
