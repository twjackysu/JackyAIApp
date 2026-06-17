import Alert from '@mui/material/Alert';

import { inferColumns } from '../../components/summarizeChart/dataPrep';
import { type ECOption } from '../../components/summarizeChart/echarts';
import { buildWaterfall } from '../dataPrep';

import { EChart } from './EChart';

interface WaterfallVizProps {
  rows: Record<string, unknown>[];
  headers: string[];
  temporalKey?: string;
}

/**
 * Bridge waterfall over time: the first period is a full bar from zero, then
 * each period is the signed change from the previous one (green up / red down).
 * Drawn with a transparent base series so the change bars float.
 */
export function WaterfallViz({ rows, headers, temporalKey }: WaterfallVizProps) {
  const { measureKeys } = inferColumns(headers, rows);
  const measure = measureKeys[0];
  if (!measure || !temporalKey)
    return <Alert severity="info">Need a time dimension and a measure.</Alert>;

  const { categories, base, rise, fall, total } = buildWaterfall(rows, temporalKey, measure);
  const manyCategories = categories.length > 12;

  const option: ECOption = {
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
    legend: { data: ['起始', '增加', '減少'], top: 0 },
    grid: {
      containLabel: true,
      left: '3%',
      right: '4%',
      top: 36,
      bottom: manyCategories ? 80 : 40,
    },
    xAxis: {
      type: 'category',
      data: categories,
      axisLabel: { rotate: manyCategories ? 35 : 0, interval: manyCategories ? 'auto' : 0 },
    },
    yAxis: { type: 'value' },
    series: [
      {
        name: 'base',
        type: 'bar',
        stack: 'wf',
        itemStyle: { color: 'transparent' },
        emphasis: { itemStyle: { color: 'transparent' } },
        tooltip: { show: false },
        data: base,
        silent: true,
      },
      { name: '起始', type: 'bar', stack: 'wf', itemStyle: { color: '#5E6AD2' }, data: total },
      { name: '增加', type: 'bar', stack: 'wf', itemStyle: { color: '#10b981' }, data: rise },
      { name: '減少', type: 'bar', stack: 'wf', itemStyle: { color: '#f43f5e' }, data: fall },
    ],
  };

  return <EChart option={option} />;
}
