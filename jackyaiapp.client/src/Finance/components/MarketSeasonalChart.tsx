import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import { useTheme } from '@mui/material/styles';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useMemo, useState } from 'react';

import {
  computeSeasonal,
  defaultRange,
  parseIsoDate,
  validateRange,
  type DateRange,
  type MonthDay,
} from '../utils/seasonal';
import { buildSeasonalOption } from '../utils/seasonalChartOption';
import {
  SEASONAL_DISCLAIMER,
  SEASONAL_NO_TRADING_DAYS,
  SEASONAL_REBASE_NOTE,
  SEASONAL_SUBJECT,
  SEASONAL_TITLE,
  SEASONAL_UNAVAILABLE,
  seasonalAsOfText,
  seasonalOmittedYearsText,
  seasonalRangeProblemText,
} from '../utils/seasonalCopy';

import type { MarketIndexHistoryData } from '@/apis/financeApis/types';

import { useGetMarketIndexHistoryQuery } from '@/apis/financeApis';
import { ECHARTS_THEME } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';

const CHART_HEIGHT = 320;

/** Line colours for the four earlier years, newest first — distinct hues that read on the app's
 * dark surface and would on a light one. The current year takes the theme's text colour. */
const EARLIER_YEAR_COLORS = ['#3B82F6', '#F59E0B', '#10B981', '#EC4899'];

const MONTHS = Array.from({ length: 12 }, (_, i) => i + 1);
// Every day 1–31 is offered for every month: choosing one that month lacks (2/30) is caught by
// validateRange and explained, rather than the picker silently rewriting the user's choice.
const DAYS = Array.from({ length: 31 }, (_, i) => i + 1);

interface MonthDayPickerProps {
  /** Accessible-name prefix: `${label}月` / `${label}日`. */
  label: string;
  value: MonthDay;
  onChange: (next: MonthDay) => void;
}

function MonthDayPicker({ label, value, onChange }: MonthDayPickerProps) {
  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
      <TextField
        select
        size="small"
        value={value.month}
        onChange={(e) => onChange({ ...value, month: Number(e.target.value) })}
        slotProps={{ select: { native: true }, htmlInput: { 'aria-label': `${label}月` } }}
      >
        {MONTHS.map((m) => (
          <option key={m} value={m}>
            {m} 月
          </option>
        ))}
      </TextField>
      <TextField
        select
        size="small"
        value={value.day}
        onChange={(e) => onChange({ ...value, day: Number(e.target.value) })}
        slotProps={{ select: { native: true }, htmlInput: { 'aria-label': `${label}日` } }}
      >
        {DAYS.map((d) => (
          <option key={d} value={d}>
            {d} 日
          </option>
        ))}
      </TextField>
    </Stack>
  );
}

function SeasonalBody({ history }: { history: MarketIndexHistoryData }) {
  const theme = useTheme();
  const defaultWindow = useMemo(() => defaultRange(history.asOf), [history.asOf]);
  // null = still on the default window, which keeps following the data's latest day.
  const [custom, setCustom] = useState<DateRange | null>(null);
  const range = custom ?? defaultWindow;
  const problem = validateRange(range);

  // Changing the range only re-runs this math on the already-fetched points — it never refetches.
  const result = useMemo(
    () => (validateRange(range) ? null : computeSeasonal(history.points, history.asOf, range)),
    [history.points, history.asOf, range],
  );

  const option = useMemo(() => {
    if (!result) return null;
    const dataYear = parseIsoDate(history.asOf)?.year ?? 0;
    return buildSeasonalOption(result, (line) =>
      line.isCurrent
        ? theme.palette.text.primary
        : EARLIER_YEAR_COLORS[(dataYear - line.year - 1) % EARLIER_YEAR_COLORS.length],
    );
  }, [result, history.asOf, theme.palette.text.primary]);

  return (
    <>
      <Stack
        direction="row"
        spacing={1}
        sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 1, mb: 1 }}
      >
        <Typography variant="body2" color="text.secondary">
          期間
        </Typography>
        <MonthDayPicker
          label="起始"
          value={range.start}
          onChange={(start) => setCustom({ ...range, start })}
        />
        <Typography variant="body2" color="text.secondary">
          ～
        </Typography>
        <MonthDayPicker
          label="結束"
          value={range.end}
          onChange={(end) => setCustom({ ...range, end })}
        />
        {custom && (
          <Button size="small" onClick={() => setCustom(null)}>
            年初至今
          </Button>
        )}
      </Stack>

      {problem ? (
        <Alert severity="warning">{seasonalRangeProblemText(problem, range)}</Alert>
      ) : result && option && result.lines.length > 0 ? (
        <>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            {SEASONAL_REBASE_NOTE}
          </Typography>
          <Box
            role="img"
            aria-label={`${SEASONAL_SUBJECT}過去 5 年同期間走勢圖，各年區間起點為 100`}
          >
            <ReactECharts
              option={option}
              notMerge
              theme={ECHARTS_THEME}
              style={{ height: CHART_HEIGHT, width: '100%' }}
            />
          </Box>
          {result.omittedYears.length > 0 && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
              {seasonalOmittedYearsText(result.omittedYears)}
            </Typography>
          )}
        </>
      ) : (
        <Alert severity="info">{SEASONAL_NO_TRADING_DAYS}</Alert>
      )}
    </>
  );
}

/**
 * Stock Intelligence's 過去 5 年同期間走勢圖: the weighted index over the same month/day window in
 * each of the last five years, one line per year, rebased to 100. A reference only — never a forecast.
 */
export const MarketSeasonalChart = () => {
  const { data: res, isError } = useGetMarketIndexHistoryQuery();
  const data = res?.data;
  // A "successful" payload that can't be drawn is as unavailable as a failed request.
  const history = data && data.points.length > 0 && parseIsoDate(data.asOf) !== null ? data : null;
  const isLoading = !data && !isError;

  return (
    <Paper
      sx={{
        p: 2,
        mt: 3,
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
        overflow: 'hidden',
      }}
    >
      <Typography variant="h6" component="h2" sx={{ fontWeight: 700 }}>
        {SEASONAL_TITLE}
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
        {SEASONAL_SUBJECT}
      </Typography>

      {history ? (
        <SeasonalBody history={history} />
      ) : isLoading ? (
        <Skeleton variant="rounded" height={CHART_HEIGHT} aria-label="載入中" />
      ) : (
        <Alert severity="warning">{SEASONAL_UNAVAILABLE}</Alert>
      )}

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>
        {seasonalAsOfText(history?.asOf ?? null)}
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {SEASONAL_DISCLAIMER}
      </Typography>
    </Paper>
  );
};

export default MarketSeasonalChart;
