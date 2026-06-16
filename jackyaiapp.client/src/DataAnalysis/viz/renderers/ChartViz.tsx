import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import EChartsCoreModule from 'echarts-for-react/lib/core';
import { useEffect, useRef } from 'react';

import { buildCartesianOption, buildPieOption } from '../../components/summarizeChart/buildOptions';
import {
  inferColumns,
  prepareCartesian,
  preparePie,
} from '../../components/summarizeChart/dataPrep';
import { ECHARTS_THEME, echarts, type ActiveChart } from '../../components/summarizeChart/echarts';

// echarts-for-react ships CJS only; the default import may arrive as the
// component or as a namespace object wrapping it.
const ReactEChartsCore =
  (EChartsCoreModule as unknown as { default?: typeof EChartsCoreModule }).default ??
  EChartsCoreModule;

interface EChartsHandle {
  getEchartsInstance: () => { resize: () => void };
}

interface ChartVizProps {
  chartType: ActiveChart;
  rows: Record<string, unknown>[];
  headers: string[];
}

/**
 * Renders one ECharts chart that fills its (resizable) container. Eligibility is
 * already enforced upstream by the matcher, so this only renders — no dimension
 * trimming or multi-dim warnings needed.
 */
export function ChartViz({ chartType, rows, headers }: ChartVizProps) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<EChartsHandle | null>(null);

  // Re-fit the chart whenever the container is resized (re-resizable changes the
  // box size; ECharts only auto-resizes on window resize by default).
  useEffect(() => {
    const el = wrapRef.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const ro = new ResizeObserver(() => chartRef.current?.getEchartsInstance().resize());
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const { groupKeys, measureKeys } = inferColumns(headers, rows);

  if (measureKeys.length === 0)
    return <Alert severity="info">No numeric measure columns — cannot render chart.</Alert>;
  if (rows.length === 0) return <Alert severity="info">No data to chart.</Alert>;

  const option =
    chartType === 'pie'
      ? buildPieOption(preparePie(rows, groupKeys, measureKeys).slices)
      : buildCartesianOption(
          chartType,
          prepareCartesian(rows, groupKeys, measureKeys),
          rows.length,
        );

  return (
    <Box ref={wrapRef} sx={{ width: '100%', height: '100%', minHeight: 160 }}>
      <ReactEChartsCore
        ref={chartRef as never}
        echarts={echarts}
        option={option}
        theme={ECHARTS_THEME}
        style={{ height: '100%', width: '100%' }}
        opts={{ renderer: 'canvas' }}
        notMerge
      />
    </Box>
  );
}
