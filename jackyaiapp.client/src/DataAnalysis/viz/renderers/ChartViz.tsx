import Alert from '@mui/material/Alert';

import { buildCartesianOption, buildPieOption } from '../../components/summarizeChart/buildOptions';
import {
  inferColumns,
  prepareCartesian,
  preparePie,
} from '../../components/summarizeChart/dataPrep';
import { type ActiveChart } from '../../components/summarizeChart/echarts';

import { EChart } from './EChart';

// Cartesian + pie/donut viz handled by this renderer.
type ChartVizType = ActiveChart | 'donut';

interface ChartVizProps {
  chartType: ChartVizType;
  rows: Record<string, unknown>[];
  headers: string[];
}

/**
 * Renders the cartesian (column/bar/line/stacked) and pie/donut charts.
 * Eligibility is already enforced upstream by the matcher, so this only renders.
 */
export function ChartViz({ chartType, rows, headers }: ChartVizProps) {
  const { groupKeys, measureKeys } = inferColumns(headers, rows);

  if (measureKeys.length === 0)
    return <Alert severity="info">No numeric measure columns — cannot render chart.</Alert>;
  if (rows.length === 0) return <Alert severity="info">No data to chart.</Alert>;

  const isPie = chartType === 'pie' || chartType === 'donut';
  const option = isPie
    ? buildPieOption(preparePie(rows, groupKeys, measureKeys).slices, {
        donut: chartType === 'donut',
      })
    : buildCartesianOption(
        chartType as Exclude<ActiveChart, 'pie'>,
        prepareCartesian(rows, groupKeys, measureKeys),
        rows.length,
      );

  return <EChart option={option} />;
}
