import Box from '@mui/material/Box';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';
import { useState } from 'react';

import { DayRange } from './dayRanges';
import { toLiquidNetWorthSeries } from './netWorthSeries';
import { MASK, usePrivacy } from './privacy';
import { RangePicker } from './RangePicker';

import { useGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME, ECOption } from '@/charts/echarts';
import CenteredSpinner from '@/components/CenteredSpinner';
import { fmtCompact, fmtNum, GAIN_COLOR, LOSS_COLOR } from '@/utils/format';

// Matches NetWorthChart's set — GetNetWorthHistoryAsync clamps to [7, 180] days server-side.
const RANGES: DayRange[] = [
  { label: '1M', days: 30 },
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
];

const LINE_COLOR = '#F59E0B';

/** Single-line 流動資產淨值 trend — deliberately its own chart rather than another series on the
 * stacked breakdown next to it. The stacked chart answers "what am I holding"; this answers "am I
 * getting better or worse off", and mixing them means the trend is read off the top edge of a
 * stack whose own composition is moving, which is exactly what made small changes unreadable.
 *
 * Real estate is excluded (see netWorthSeries.liquidNetWorth) so a static multi-million-dollar
 * house can't flatten the line everything else moves on. */
export function NetWorthTrendChart() {
  const { mode } = usePrivacy();
  const [days, setDays] = useState(90);
  // groupBy=assetType is required, not incidental: liquidNetWorth keys off the "Property" group,
  // which only exists under that mode (subCategory/holding label it 不動產 instead). RTK Query
  // dedupes this against NetWorthChart's own fetch whenever it's on the same mode and range.
  const {
    data: res,
    isLoading,
    isFetching,
  } = useGetNetWorthHistoryQuery({ days, groupBy: 'assetType' });

  const series = toLiquidNetWorthSeries(res?.data ?? [], 'daily');

  if (isLoading) {
    return <CenteredSpinner sx={{ py: 6 }} />;
  }

  const latest = series.length > 0 ? series[series.length - 1].value : null;
  const first = series.length > 0 ? series[0].value : null;
  const periodChange = latest != null && first != null ? latest - first : null;

  const option: ECOption = {
    tooltip: {
      trigger: 'axis',
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = (params as { axisValue: string; value: number }[])[0];
        if (!p) return '';
        return `<b>${p.axisValue}</b><br/>流動資產淨值：${
          mode === 'full' ? `NT$${fmtNum(p.value)}` : MASK
        }`;
      },
    },
    grid: { top: 16, right: 16, bottom: 24, left: mode === 'full' ? 64 : 24 },
    xAxis: { type: 'category', data: series.map((s) => s.date), boundaryGap: false },
    yAxis: {
      type: 'value',
      // Not zero-based: net worth rarely goes near 0, and anchoring there flattens the very
      // movement this chart exists to show. Same reasoning as the waterfall's axis.
      scale: true,
      axisLabel: { formatter: (v: number) => (mode === 'full' ? fmtCompact(v) : '') },
    },
    series: [
      {
        name: '流動資產淨值',
        type: 'line',
        data: series.map((s) => s.value),
        smooth: true,
        symbol: 'none',
        lineStyle: { width: 2, color: LINE_COLOR },
        itemStyle: { color: LINE_COLOR },
        areaStyle: { color: LINE_COLOR, opacity: 0.12 },
      },
    ],
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1 }}>
        <RangePicker ranges={RANGES} value={days} onChange={setDays} />
      </Box>

      {isFetching && <LinearProgress sx={{ mb: 1 }} />}

      {series.length === 0 ? (
        <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
          資料不足，更新持倉後即可看到淨值走勢
        </Typography>
      ) : (
        <>
          {/* The period change is a delta, so it stays readable in 只看損益 mode where the
              absolute figure beside it is masked. */}
          {periodChange != null && mode !== 'hidden' && (
            <Typography variant="body2" sx={{ mb: 1 }}>
              <Box component="span" sx={{ color: 'text.secondary' }}>
                期間變化{' '}
              </Box>
              <Box
                component="span"
                sx={{ fontWeight: 700, color: periodChange >= 0 ? GAIN_COLOR : LOSS_COLOR }}
              >
                {periodChange >= 0 ? '+' : '−'}NT${fmtNum(Math.abs(periodChange))}
              </Box>
            </Typography>
          )}
          <ReactEcharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 240 }} />
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
            流動資產淨值 = 流動資產 − 負債，不含不動產。
          </Typography>
        </>
      )}
    </Box>
  );
}
