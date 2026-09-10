import EChartsCoreModule from 'echarts-for-react/lib/core';

import { echarts } from './echarts';

import type { EChartsReactProps } from 'echarts-for-react/lib/types';
import type { Ref } from 'react';

// echarts-for-react ships CJS only; the default import may arrive as the component
// itself or as a namespace object wrapping it.
const EChartsCore = ((EChartsCoreModule as unknown as { default?: typeof EChartsCoreModule })
  .default ?? EChartsCoreModule) as typeof EChartsCoreModule;

export type ReactEChartsProps = Omit<EChartsReactProps, 'echarts'> & {
  ref?: Ref<EChartsCoreModule>;
};

/**
 * The app's only ECharts renderer. It binds the tree-shaken `echarts/core` build registered in
 * ./echarts — importing the `echarts-for-react` entry point directly instead drags the *full*
 * ECharts distribution (every chart type, component and renderer) into the importing route's
 * chunk, which is what it used to do for Portfolio.
 *
 * A chart type or component that isn't in ./echarts's `echarts.use([...])` list renders as an
 * empty canvas, so add it there before using it.
 */
export default function ReactECharts({ ref, ...props }: ReactEChartsProps) {
  return <EChartsCore ref={ref} echarts={echarts} opts={{ renderer: 'canvas' }} {...props} />;
}
