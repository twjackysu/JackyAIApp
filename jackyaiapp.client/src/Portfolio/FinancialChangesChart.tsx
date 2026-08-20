import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonGroup from '@mui/material/ButtonGroup';
import CircularProgress from '@mui/material/CircularProgress';
import LinearProgress from '@mui/material/LinearProgress';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';
import { useState } from 'react';

import { DayRange } from './dayRanges';
import {
  barCountsByGranularity,
  Granularity,
  MIN_USEFUL_BARS,
  toDeltas,
  toLiquidNetWorthSeries,
} from './netWorthSeries';
import { MASK, usePrivacy } from './privacy';
import { RangePicker } from './RangePicker';

import { useGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME, ECOption } from '@/charts/echarts';
import { fmtCompact, fmtNum } from '@/utils/format';

const RANGES: DayRange[] = [
  { label: '1M', days: 30 },
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
];

const GRANULARITIES: { label: string; value: Granularity }[] = [
  { label: '日', value: 'daily' },
  { label: '週', value: 'weekly' },
  { label: '月', value: 'monthly' },
];

const GAIN_COLOR = '#22c55e';
const LOSS_COLOR = '#ef4444';

/** Period-over-period change in 流動資產淨值, on its own axis.
 *
 * This is the chart that actually solves the reported problem: at ~10M of assets against ~8M of
 * debt, a +20k/-10k month is a rounding error on an absolute-value axis and invisible. Differencing
 * first, then letting the axis scale to the differences, makes that month a full-height bar.
 *
 * Granularity is user-switchable rather than fixed because the underlying data is daily and 180
 * daily bars is its own kind of unreadable — the noise just moves rather than going away. */
export function FinancialChangesChart() {
  const { mode } = usePrivacy();
  const [days, setDays] = useState(180);
  const [granularity, setGranularity] = useState<Granularity>('weekly');
  const {
    data: res,
    isLoading,
    isFetching,
  } = useGetNetWorthHistoryQuery({ days, groupBy: 'assetType' });

  const points = res?.data ?? [];
  // Which granularities the current range can actually support. The backend caps the window at
  // 180 days (both price feeds only carry 6 months, so a longer window would understate rather
  // than extend), which makes e.g. 1-month range + monthly buckets degenerate to a single bar.
  const barCounts = barCountsByGranularity(points);
  const effectiveGranularity =
    barCounts[granularity] >= MIN_USEFUL_BARS
      ? granularity
      : // Fall back to the coarsest granularity that still says something, rather than rendering
        // one lone bar that reads as a broken chart.
        ((['monthly', 'weekly', 'daily'] as Granularity[]).find(
          (g) => barCounts[g] >= MIN_USEFUL_BARS,
        ) ?? 'daily');

  // Bucket first, then difference — differencing daily and summing would give the same answer for
  // a stock quantity but is more work and loses the end-of-period reading.
  const deltas = toDeltas(toLiquidNetWorthSeries(points, effectiveGranularity));
  // The first bucket has no predecessor, so it carries no bar. Dropping it keeps the axis honest
  // rather than rendering a gap the reader has to interpret.
  const plotted = deltas.filter((d) => d.delta != null);

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  const option: ECOption = {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = (params as { axisValue: string; value: number }[])[0];
        if (!p) return '';
        const sign = p.value >= 0 ? '+' : '−';
        return `<b>${p.axisValue}</b><br/>淨值變化：${
          mode !== 'hidden' ? `${sign}NT$${fmtNum(Math.abs(p.value))}` : MASK
        }`;
      },
    },
    grid: { top: 16, right: 16, bottom: 32, left: mode !== 'hidden' ? 64 : 24 },
    xAxis: {
      type: 'category',
      data: plotted.map((d) => d.date),
      axisLabel: { rotate: plotted.length > 12 ? 30 : 0, fontSize: 11 },
    },
    yAxis: {
      type: 'value',
      // Scaled to the deltas alone — never to the absolute balances they came from. This is the
      // entire point of the chart; anchoring to the asset scale would reproduce the original bug.
      axisLabel: { formatter: (v: number) => (mode !== 'hidden' ? fmtCompact(v) : '') },
    },
    series: [
      {
        name: '淨值變化',
        type: 'bar',
        data: plotted.map((d) => ({
          value: d.delta as number,
          itemStyle: { color: (d.delta as number) >= 0 ? GAIN_COLOR : LOSS_COLOR },
        })),
        barMaxWidth: 40,
      },
    ],
  };

  return (
    <Box>
      <Box
        sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, justifyContent: 'space-between', mb: 1 }}
      >
        <ButtonGroup size="small" variant="outlined">
          {GRANULARITIES.map((g) => {
            // Disabled rather than hidden: a missing button looks like a bug, whereas a disabled
            // one with a reason explains why this range can't support that bucket size.
            const bars = barCounts[g.value];
            const usable = bars >= MIN_USEFUL_BARS;
            return (
              <Tooltip
                key={g.value}
                title={
                  usable ? '' : `目前範圍只有 ${bars} 根，改選更長的範圍才看得出${g.label}變化`
                }
              >
                {/* span wrapper: MUI tooltips need a non-disabled child to receive hover events */}
                <span>
                  <Button
                    onClick={() => setGranularity(g.value)}
                    disabled={!usable}
                    variant={effectiveGranularity === g.value ? 'contained' : 'outlined'}
                  >
                    {g.label}
                  </Button>
                </span>
              </Tooltip>
            );
          })}
        </ButtonGroup>
        <RangePicker ranges={RANGES} value={days} onChange={setDays} />
      </Box>

      {isFetching && <LinearProgress sx={{ mb: 1 }} />}

      {plotted.length === 0 ? (
        <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
          資料不足 —— 至少需要兩個
          {GRANULARITIES.find((g) => g.value === effectiveGranularity)?.label}的紀錄才能計算變化量
        </Typography>
      ) : (
        <>
          <ReactEcharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 240 }} />
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
            每期期末流動資產淨值與前一期的差額。Y
            軸依變化量自行縮放，不受資產絕對金額影響——這正是小額變化在上方走勢圖看不出來的原因。
            範圍最多 6 個月：歷史股價只取得到近 6 個月，再往前的淨值無法正確計算。
          </Typography>
        </>
      )}
    </Box>
  );
}
