import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonGroup from '@mui/material/ButtonGroup';
import CircularProgress from '@mui/material/CircularProgress';
import Grid from '@mui/material/Grid';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';
import { useState } from 'react';

import { MASK, usePrivacy } from './privacy';

import { useGetBenchmarkComparisonQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME, ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

const RANGES: { label: string; days: number }[] = [
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
  { label: '1Y', days: 365 },
  { label: '3Y', days: 1095 },
];

const PORTFOLIO_COLOR = '#3B82F6';
const BENCHMARK_COLOR = '#9CA3AF';

function ReturnStat({ label, pct, color }: { label: string; pct: number | null; color: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {label}
      </Typography>
      <Typography variant="h6" sx={{ fontWeight: 700, color }}>
        {pct != null ? `${pct >= 0 ? '+' : ''}${pct.toFixed(2)}%` : '—'}
      </Typography>
    </Box>
  );
}

export function BenchmarkChart() {
  const { mode } = usePrivacy();
  const [days, setDays] = useState(365);
  const { data: res, isLoading, isFetching } = useGetBenchmarkComparisonQuery({ days });
  const comparison = res?.data;
  const points = comparison?.points ?? [];

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
        資料不足，更新持倉（並填入成本）後即可看到大盤對照
      </Typography>
    );
  }

  const benchmarkLabel = comparison!.benchmarkSymbol;
  const outperforming =
    comparison!.portfolioReturnPct != null &&
    comparison!.benchmarkReturnPct != null &&
    comparison!.portfolioReturnPct >= comparison!.benchmarkReturnPct;

  const option: ECOption = {
    tooltip: {
      trigger: 'axis',
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = params as { axisValue: string; seriesName: string; value: number }[];
        const date = p[0]?.axisValue ?? '';
        const lines = p.map((s) =>
          mode === 'full'
            ? `${s.seriesName}：NT$${s.value.toLocaleString('zh-TW')}`
            : `${s.seriesName}：${MASK}`,
        );
        return [`<b>${date}</b>`, ...lines].join('<br/>');
      },
    },
    legend: {
      data: ['我的投資組合', benchmarkLabel],
      textStyle: { fontSize: 11 },
    },
    grid: { top: 32, right: 16, bottom: 24, left: mode === 'full' ? 56 : 24 },
    xAxis: {
      type: 'category',
      data: points.map((p) => p.date),
      boundaryGap: false,
    },
    yAxis: {
      type: 'value',
      // Mask the scale numbers unless fully visible — matches NetWorthChart's convention.
      axisLabel: {
        formatter: (v: number) =>
          mode !== 'full'
            ? ''
            : Math.abs(v) >= 1_000_000
              ? `${(v / 1_000_000).toFixed(1)}M`
              : `${v}`,
      },
    },
    series: [
      {
        name: '我的投資組合',
        type: 'line',
        data: points.map((p) => p.portfolioTWD),
        smooth: true,
        symbol: 'none',
        lineStyle: { width: 2, color: PORTFOLIO_COLOR },
        itemStyle: { color: PORTFOLIO_COLOR },
      },
      {
        name: benchmarkLabel,
        type: 'line',
        data: points.map((p) => p.benchmarkTWD),
        smooth: true,
        symbol: 'none',
        lineStyle: { width: 2, color: BENCHMARK_COLOR, type: 'dashed' },
        itemStyle: { color: BENCHMARK_COLOR },
      },
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

      {isFetching && <LinearProgress sx={{ mb: 1 }} />}

      <Grid container spacing={2} sx={{ mb: 2 }}>
        <Grid size={{ xs: 6, sm: 4 }}>
          <ReturnStat
            label="我的投資組合"
            pct={comparison!.portfolioReturnPct}
            color={PORTFOLIO_COLOR}
          />
        </Grid>
        <Grid size={{ xs: 6, sm: 4 }}>
          <ReturnStat
            label={benchmarkLabel}
            pct={comparison!.benchmarkReturnPct}
            color={BENCHMARK_COLOR}
          />
        </Grid>
        {comparison!.portfolioReturnPct != null && comparison!.benchmarkReturnPct != null && (
          <Grid size={{ xs: 12, sm: 4 }}>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
              相對表現
            </Typography>
            <Typography
              variant="h6"
              sx={{ fontWeight: 700, color: outperforming ? '#22c55e' : '#ef4444' }}
            >
              {outperforming ? '+' : ''}
              {(comparison!.portfolioReturnPct - comparison!.benchmarkReturnPct).toFixed(2)}%
              {outperforming ? ' 贏大盤' : ' 輸大盤'}
            </Typography>
          </Grid>
        )}
      </Grid>

      <ReactEcharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 260 }} />

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
        模擬「若把每一筆投入成本都拿去買 {benchmarkLabel}
        」的對照線——僅計算已填成本的股票/基金持倉，兩條線都不含股息（純價格報酬對照）。
      </Typography>
    </Box>
  );
}
