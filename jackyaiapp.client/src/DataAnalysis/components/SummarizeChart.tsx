import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import EChartsCoreModule from 'echarts-for-react/lib/core';

import { buildCartesianOption, buildPieOption } from './summarizeChart/buildOptions';
import { inferColumns, prepareCartesian, preparePie } from './summarizeChart/dataPrep';
import {
  CHART_HEIGHT,
  CLUTTER_THRESHOLD,
  echarts,
  type ActiveChart,
} from './summarizeChart/echarts';

export type { ChartViewType } from './summarizeChart/echarts';

// echarts-for-react ships CJS only; depending on how the bundler pre-optimizes it,
// the default import may arrive as the component itself or as a module namespace
// object wrapping it ({ default: Component }).
const ReactEChartsCore =
  (EChartsCoreModule as unknown as { default?: typeof EChartsCoreModule }).default ??
  EChartsCoreModule;

interface SummarizeChartProps {
  chartType: ActiveChart;
  rows: Record<string, unknown>[];
  headers: string[];
}

export function SummarizeChart({ chartType, rows, headers }: SummarizeChartProps) {
  const { groupKeys, measureKeys } = inferColumns(headers, rows);

  if (measureKeys.length === 0)
    return <Alert severity="info">No numeric measure columns — cannot render chart.</Alert>;
  if (rows.length === 0) return <Alert severity="info">No data to chart.</Alert>;

  // ── Pie: separate code path ───────────────────────────────────────────────
  if (chartType === 'pie') {
    const { slices, multiDimWarning } = preparePie(rows, groupKeys, measureKeys);
    const option = buildPieOption(slices);

    return (
      <Box>
        {measureKeys.length > 1 && (
          <Alert severity="info" sx={{ mb: 1 }}>
            Pie shows the first measure only: <strong>{measureKeys[0]}</strong>.
          </Alert>
        )}
        {multiDimWarning && (
          <Alert severity="info" sx={{ mb: 1 }}>
            Pie uses only the first group-by (<strong>{groupKeys[0]}</strong>). Values are summed
            when multiple rows share the same category.
          </Alert>
        )}
        {rows.length > CLUTTER_THRESHOLD && (
          <Alert severity="warning" sx={{ mb: 1 }}>
            {slices.length} slices — pie charts are hard to read with many categories.
          </Alert>
        )}
        <Paper variant="outlined" sx={{ p: 1 }}>
          <ReactEChartsCore
            echarts={echarts}
            option={option}
            style={{ height: CHART_HEIGHT }}
            opts={{ renderer: 'canvas' }}
            notMerge
          />
        </Paper>
      </Box>
    );
  }

  // ── Cartesian charts (column / bar / line / stacked*) ─────────────────────
  const cartesian = prepareCartesian(rows, groupKeys, measureKeys);
  const option = buildCartesianOption(chartType, cartesian, rows.length);

  return (
    <Box>
      {cartesian.dimsTrimmed && (
        <Alert severity="warning" sx={{ mb: 1 }}>
          Chart view supports at most 2 group-by fields. Only <strong>{groupKeys[0]}</strong>{' '}
          (X-axis) and <strong>{groupKeys[1]}</strong> (series) are shown. Switch to table view to
          see all dimensions.
        </Alert>
      )}
      {groupKeys.length === 2 && (
        <Alert severity="info" sx={{ mb: 1 }}>
          X-axis: <strong>{groupKeys[0]}</strong> · Series: <strong>{groupKeys[1]}</strong>
        </Alert>
      )}
      {rows.length > CLUTTER_THRESHOLD && (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {rows.length} data points — consider adding filters or a coarser group-by for a cleaner
          chart.
        </Alert>
      )}
      <Paper variant="outlined" sx={{ p: 1 }}>
        <ReactEChartsCore
          echarts={echarts}
          option={option}
          style={{ height: CHART_HEIGHT }}
          opts={{ renderer: 'canvas' }}
          notMerge
        />
      </Paper>
    </Box>
  );
}
