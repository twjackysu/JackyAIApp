import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import LinearProgress from '@mui/material/LinearProgress';
import { useTheme } from '@mui/material/styles';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { lazy, Suspense, useState } from 'react';

import { applyEdit, DateRange, defaultRange, firstRecordDate, localToday } from './netWorthRange';
import { toLiquidNetWorthSeries } from './netWorthSeries';
import { MASK, usePrivacy } from './privacy';

import { useGetNetWorthHistoryQuery, useGetUpdateHistoryQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME, ECOption } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';
import CenteredSpinner from '@/components/CenteredSpinner';
import { escapeHtml } from '@/components/HtmlViewer/escapeHtml';
import { fmtCompact, fmtNum, GAIN_COLOR, LOSS_COLOR } from '@/utils/format';

const LINE_COLOR = '#F59E0B';

// The picker (MUI X + dayjs) is about 49 kB gzipped — as much again as the rest of the Portfolio
// chunk. This chart only mounts once scrolled into view (LazyMount), so splitting the picker out
// keeps that cost off the page's first load.
const IsoDatePicker = lazy(() =>
  import('./IsoDatePicker').then((m) => ({ default: m.IsoDatePicker })),
);

/** Same footprint as the picker while its chunk loads, so the controls don't jump. */
function DateFieldPlaceholder({
  label,
  value,
  size,
}: {
  label: string;
  value: string;
  size: 'small' | 'medium';
}) {
  return (
    <TextField
      label={label}
      value={value.replace(/-/g, '/')}
      size={size}
      fullWidth
      disabled
      slotProps={{ inputLabel: { shrink: true } }}
    />
  );
}

/** Single-line 流動資產淨值 trend — deliberately its own chart rather than another series on the
 * stacked breakdown next to it. The stacked chart answers "what am I holding"; this answers "am I
 * getting better or worse off", and mixing them means the trend is read off the top edge of a
 * stack whose own composition is moving, which is exactly what made small changes unreadable.
 *
 * Real estate is excluded (see netWorthSeries.liquidNetWorth) so a static multi-million-dollar
 * house can't flatten the line everything else moves on. */
export function NetWorthTrendChart() {
  const { mode } = usePrivacy();
  const today = localToday();

  // The first snapshot date bounds the start input and shapes the default window. Same query the
  // update-history dialog uses, so it is shared from cache rather than fetched twice.
  const { data: historyRes, isLoading: historyLoading } = useGetUpdateHistoryQuery();
  const firstRecord = firstRecordDate((historyRes?.data ?? []).map((h) => h.effectiveDate));

  // null = still on the default, which follows `firstRecord` as it loads and `today` as days pass.
  // Only an edit pins a window.
  const [picked, setPicked] = useState<DateRange | null>(null);
  const range = picked ?? defaultRange(today, firstRecord);

  // On a phone the two dates sit side by side at half width each (or stacked when too narrow),
  // full-size for touch. At the desktop's fixed width they don't fit beside the card padding on a
  // ~375px screen and wrap raggedly to the right.
  const theme = useTheme();
  const narrow = useMediaQuery(theme.breakpoints.down('sm'), { noSsr: true });
  const fieldSize = narrow ? 'medium' : 'small';

  // groupBy=assetType is required, not incidental: liquidNetWorth keys off the "Property" group,
  // which only exists under that mode (subCategory/holding label it 不動產 instead).
  // Waits for the first record date so the default window is fetched once, not once as "a month
  // back" and again as "from the first record".
  const {
    data: res,
    isLoading: chartLoading,
    isFetching,
  } = useGetNetWorthHistoryQuery(
    { from: range.from, to: range.to, groupBy: 'assetType' },
    { skip: historyLoading },
  );
  const isLoading = historyLoading || chartLoading;

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
        return `<b>${escapeHtml(p.axisValue)}</b><br/>流動資產淨值：${
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
      <Box
        sx={{
          display: { xs: 'grid', sm: 'flex' },
          // Two columns while each can still show a whole date (2026/09/03 needs ~110px with the
          // field's padding); on narrower screens the dates stack instead of clipping the day.
          gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))',
          justifyContent: 'flex-end',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 1,
          mb: 1,
        }}
      >
        {/* Both span first record → today; ordering and the one-year limit are kept by moving
            the *other* date (applyEdit), so any date in that span can be picked directly. */}
        <Box sx={{ width: { xs: 'auto', sm: 170 }, minWidth: 0 }}>
          <Suspense
            fallback={<DateFieldPlaceholder label="起始日" value={range.from} size={fieldSize} />}
          >
            <IsoDatePicker
              label="起始日"
              value={range.from}
              min={firstRecord}
              max={today}
              onChange={(v) => setPicked(applyEdit(range, 'from', v))}
              size={fieldSize}
              fullWidth
            />
          </Suspense>
        </Box>
        <Box sx={{ width: { xs: 'auto', sm: 170 }, minWidth: 0 }}>
          <Suspense
            fallback={<DateFieldPlaceholder label="結束日" value={range.to} size={fieldSize} />}
          >
            <IsoDatePicker
              label="結束日"
              value={range.to}
              min={firstRecord}
              max={today}
              onChange={(v) => setPicked(applyEdit(range, 'to', v))}
              size={fieldSize}
              fullWidth
            />
          </Suspense>
        </Box>
        {picked && (
          <Button
            size="small"
            onClick={() => setPicked(null)}
            sx={{ gridColumn: '1 / -1', justifySelf: 'end' }}
          >
            近一個月
          </Button>
        )}
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
          <ReactECharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 240 }} />
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
            流動資產淨值 = 流動資產 −
            負債，不含不動產。期間最長一年，超過時另一個日期會自動跟著調整。
          </Typography>
        </>
      )}
    </Box>
  );
}
