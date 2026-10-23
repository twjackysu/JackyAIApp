import { useCallback, useLayoutEffect, useMemo, useRef, useState } from 'react';

import {
  measureFrame,
  projectionOf,
  sameFrame,
  type ChartFrame,
  type PixelConverter,
} from '../trendlineEditor/chartProjection';

import type { PriceRange } from '../../utils/trendlineChartOption';

export interface EChartsHandle {
  getEchartsInstance: () => PixelConverter;
}

/**
 * Where the plot is on screen, measured from the chart, so the overlay can draw over it. Pass
 * `chartRef`, `onEvents` and `onChartReady` to the chart.
 */
export function useChartFrame(dates: string[], priceRange: PriceRange) {
  const chartRef = useRef<EChartsHandle | null>(null);
  const [frame, setFrame] = useState<ChartFrame | null>(null);
  const remeasure = useRef(() => {});
  // ECharts lays the chart out synchronously when it gets the option, which happens in the
  // chart's own commit, before this effect: so every render can measure what is on screen. A
  // resize re-lays it out without a render here, hence also the `finished` event below.
  useLayoutEffect(() => {
    remeasure.current = () => {
      const chart = chartRef.current?.getEchartsInstance();
      if (!chart) return;
      const next = measureFrame(chart, dates, priceRange);
      setFrame((prev) => (sameFrame(prev, next) ? prev : next));
    };
    remeasure.current();
  });
  // Stable, or echarts-for-react would rebuild the chart on every render. echarts-for-react
  // sets the option only once the chart's box has a size, so the first measure that can succeed
  // is when it reports the chart ready.
  const onEvents = useMemo(() => ({ finished: () => remeasure.current() }), []);
  const onChartReady = useCallback(() => remeasure.current(), []);
  const projection = useMemo(() => (frame ? projectionOf(frame) : null), [frame]);
  return { chartRef, onEvents, onChartReady, projection };
}
