import Box from '@mui/material/Box';
import EChartsCoreModule from 'echarts-for-react/lib/core';
import { useEffect, useRef } from 'react';

import { ECHARTS_THEME, echarts, type ECOption } from '../../components/summarizeChart/echarts';

// echarts-for-react ships CJS only; the default import may arrive as the
// component or as a namespace object wrapping it.
const ReactEChartsCore =
  (EChartsCoreModule as unknown as { default?: typeof EChartsCoreModule }).default ??
  EChartsCoreModule;

interface EChartsHandle {
  getEchartsInstance: () => { resize: () => void };
}

interface EChartProps {
  option: ECOption;
}

/**
 * Renders an ECharts option filling its (resizable) container, re-fitting on
 * container resize. Shared by all chart-style viz renderers.
 */
export function EChart({ option }: EChartProps) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<EChartsHandle | null>(null);

  useEffect(() => {
    const el = wrapRef.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const ro = new ResizeObserver(() => chartRef.current?.getEchartsInstance().resize());
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

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
