import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { BENCHMARK_RETURN_TOOLTIP } from './benchmarkReturn';
import { DEFAULT_WIDE_RANGE_DAYS, WIDE_RANGES } from './dayRanges';
import { HistoryLimitNote } from './HistoryLimitNote';
import { MASK, usePrivacy } from './privacy';
import { RangePicker } from './RangePicker';
import { StatBlock } from './StatBlock';

import { useGetBenchmarkComparisonQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME, ECOption } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';
import CenteredSpinner from '@/components/CenteredSpinner';
import { InfoPopover } from '@/components/hints';
import { fmtCompact, fmtNum, fmtSignedPct } from '@/utils/format';

// QQQ/VOO/VT track the Nasdaq-100/S&P500/global-stock indices closely enough via their
// respective ETFs — same "ETF as index proxy" logic already used for 0050 (Taiwan 50).
const BENCHMARK_PRESETS: { symbol: string; label: string; color: string }[] = [
  { symbol: '0050', label: '台股 0050', color: '#9CA3AF' },
  { symbol: 'QQQ', label: '那斯達克 QQQ', color: '#F59E0B' },
  { symbol: 'VOO', label: 'S&P500 VOO', color: '#10B981' },
  { symbol: 'VT', label: '全球 VT', color: '#EC4899' },
];
const PRESET_COLOR = new Map(BENCHMARK_PRESETS.map((p) => [p.symbol, p.color]));

const PORTFOLIO_COLOR = '#3B82F6';

function ReturnStat({ label, pct, color }: { label: string; pct: number | null; color: string }) {
  return (
    <StatBlock
      label={label}
      value={pct != null ? fmtSignedPct(pct) : '—'}
      color={color}
      valueVariant="h6"
    />
  );
}

export function BenchmarkChart() {
  const { mode } = usePrivacy();
  const [days, setDays] = useState(DEFAULT_WIDE_RANGE_DAYS);
  const [symbols, setSymbols] = useState<string[]>(['0050']);
  const { data: res, isLoading, isFetching } = useGetBenchmarkComparisonQuery({ days, symbols });
  const comparison = res?.data;
  const dates = comparison?.dates ?? [];

  const toggleSymbol = (symbol: string) =>
    setSymbols((prev) => {
      if (prev.includes(symbol)) {
        // Always keep at least one benchmark selected — an empty request just falls back to the
        // backend default (0050) anyway, which would silently ignore the user's deselection.
        if (prev.length === 1) return prev;
        return prev.filter((s) => s !== symbol);
      }
      return [...prev, symbol];
    });

  if (isLoading) {
    return <CenteredSpinner sx={{ py: 6 }} />;
  }

  const benchmarks = comparison?.benchmarks ?? [];

  const option: ECOption = {
    tooltip: {
      trigger: 'axis',
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = params as { axisValue: string; seriesName: string; value: number }[];
        const date = p[0]?.axisValue ?? '';
        const lines = p.map((s) =>
          mode === 'full' ? `${s.seriesName}：NT$${fmtNum(s.value)}` : `${s.seriesName}：${MASK}`,
        );
        return [`<b>${date}</b>`, ...lines].join('<br/>');
      },
    },
    legend: {
      data: ['我的投資組合', ...benchmarks.map((b) => b.symbol)],
      textStyle: { fontSize: 11 },
    },
    grid: { top: 40, right: 16, bottom: 24, left: mode === 'full' ? 56 : 24 },
    xAxis: {
      type: 'category',
      data: dates,
      boundaryGap: false,
    },
    yAxis: {
      type: 'value',
      // Mask the scale numbers unless fully visible — matches NetWorthChart's convention.
      axisLabel: {
        formatter: (v: number) => (mode !== 'full' ? '' : fmtCompact(v)),
      },
    },
    series: [
      {
        name: '我的投資組合',
        type: 'line',
        data: comparison?.portfolioTWD ?? [],
        smooth: true,
        symbol: 'none',
        lineStyle: { width: 2, color: PORTFOLIO_COLOR },
        itemStyle: { color: PORTFOLIO_COLOR },
      },
      ...benchmarks.map((b) => {
        const color = PRESET_COLOR.get(b.symbol) ?? '#9CA3AF';
        return {
          name: b.symbol,
          type: 'line' as const,
          data: b.valuesTWD,
          smooth: true,
          symbol: 'none' as const,
          lineStyle: { width: 2, color, type: 'dashed' as const },
          itemStyle: { color },
        };
      }),
    ],
  };

  return (
    <Box>
      <Box
        sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, justifyContent: 'space-between', mb: 2 }}
      >
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
          {BENCHMARK_PRESETS.map((preset) => (
            <Chip
              key={preset.symbol}
              label={preset.label}
              size="small"
              onClick={() => toggleSymbol(preset.symbol)}
              variant={symbols.includes(preset.symbol) ? 'filled' : 'outlined'}
              sx={{
                borderColor: preset.color,
                ...(symbols.includes(preset.symbol) && {
                  bgcolor: preset.color,
                  color: '#fff',
                  '&:hover': { bgcolor: preset.color },
                }),
              }}
            />
          ))}
        </Box>
        <RangePicker ranges={WIDE_RANGES} value={days} onChange={setDays} />
      </Box>

      <HistoryLimitNote days={days} />

      {isFetching && <LinearProgress sx={{ mb: 1 }} />}

      {dates.length === 0 ? (
        <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
          資料不足，更新持倉（並填入成本）後即可看到大盤對照
        </Typography>
      ) : (
        <>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, mb: 0.5 }}>
            <Typography variant="caption" color="text.secondary">
              區間報酬（含息，時間加權，未年化）
            </Typography>
            <InfoPopover
              content={BENCHMARK_RETURN_TOOLTIP}
              ariaLabel="區間報酬計算方式說明"
              iconSize={14}
            />
          </Box>
          <Grid container spacing={2} sx={{ mb: 2 }}>
            <Grid size={{ xs: 6, sm: 3 }}>
              <ReturnStat
                label="我的投資組合"
                pct={comparison!.portfolioReturnPct}
                color={PORTFOLIO_COLOR}
              />
            </Grid>
            {benchmarks.map((b) => (
              <Grid size={{ xs: 6, sm: 3 }} key={b.symbol}>
                <ReturnStat
                  label={b.symbol}
                  pct={b.returnPct}
                  color={PRESET_COLOR.get(b.symbol) ?? '#9CA3AF'}
                />
              </Grid>
            ))}
          </Grid>

          <ReactECharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 280 }} />

          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
            模擬「若把每一筆投入成本都拿去買該標的」的對照線。線是部位市值（不含息），會隨加碼變高；上面的
            % 是含息報酬且已排除加碼影響，所以和線的漲幅不會一致。
          </Typography>
        </>
      )}
    </Box>
  );
}
