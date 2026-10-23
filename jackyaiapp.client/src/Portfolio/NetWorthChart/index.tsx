import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonGroup from '@mui/material/ButtonGroup';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { NET_WORTH_RANGES } from '../dayRanges';
import { usePrivacy } from '../privacy';
import { RangePicker } from '../RangePicker';

import {
  breakdownOf,
  pointsForView,
  queryGroupByOf,
  VIEW_BY_OPTIONS,
  type ViewBy,
} from './breakdown';
import { buildNetWorthOption } from './chartOption';
import { NetWorthLegend } from './NetWorthLegend';

import { useGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';
import CenteredSpinner from '@/components/CenteredSpinner';

/** 資產與負債組成: the net worth history as a stack of its groups, by the view picked. */
export function NetWorthChart() {
  const [days, setDays] = useState(90);
  const [groupBy, setGroupBy] = useState<ViewBy>('assetType');
  // Series names the user has clicked off in the custom legend below — ECharts' legend component
  // still drives series show/hide via `legend.selected` even with `legend.show: false`, so this
  // stays fully interactive without bringing back the native legend UI (pagination/overlap).
  const [hiddenNames, setHiddenNames] = useState<Set<string>>(new Set());
  const toggleName = (name: string) =>
    setHiddenNames((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  const { mode } = usePrivacy();
  // isFetching (not just isLoading): switching view-by/range to a groupBy+days combo that isn't
  // cached yet fires a real request — isLoading alone only covers the very first ever fetch, so a
  // later switch to a not-yet-cached combo showed no loading affordance at all and just looked
  // broken/stale until the data quietly arrived.
  const {
    data: res,
    isLoading,
    isFetching,
  } = useGetNetWorthHistoryQuery({
    days,
    groupBy: queryGroupByOf(groupBy),
  });
  const points = pointsForView(res?.data ?? [], groupBy);

  if (isLoading) {
    return <CenteredSpinner sx={{ py: 6 }} />;
  }

  if (points.length === 0) {
    // Still resolving a not-yet-cached combo (belt-and-suspenders alongside the isLoading check
    // above) — show the spinner instead of prematurely claiming there's no data.
    if (isFetching) {
      return <CenteredSpinner sx={{ py: 6 }} />;
    }
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        資料不足，更新持倉後即可看到圖表
      </Typography>
    );
  }

  const breakdown = breakdownOf(points);
  const option = buildNetWorthOption(points, breakdown, mode, hiddenNames);

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
        <RangePicker ranges={NET_WORTH_RANGES} value={days} onChange={setDays} />
      </Box>
      {/* Cached data for this exact (days, groupBy) combo is stale and being revalidated in the
          background, OR we already fell through the isLoading gate above but a slower network
          still has the request in flight — either way, some visible feedback beats the chart just
          silently sitting there looking frozen/wrong until it updates. */}
      {isFetching && <LinearProgress sx={{ mb: 1 }} />}
      {/* notMerge: without it, ECharts incrementally patches the previous option onto the new one
          rather than replacing it outright — switching between view-by modes with very different
          series (e.g. subCategory's free-form AI labels vs assetType's fixed set) left stale
          series names lingering in the tooltip from whatever was hovered under the previous mode. */}
      <ReactECharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 280 }} />
      <NetWorthLegend breakdown={breakdown} hiddenNames={hiddenNames} onToggle={toggleName} />
    </Box>
  );
}
