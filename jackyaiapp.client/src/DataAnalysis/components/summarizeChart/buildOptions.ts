import { ANGLE_THRESHOLD, PALETTE, type ActiveChart, type ECOption } from './echarts';

import type { CartesianData, PieData } from './dataPrep';
import type { BarSeriesOption, LineSeriesOption, PieSeriesOption } from 'echarts/charts';
import type { GridComponentOption, LegendComponentOption } from 'echarts/components';

export function buildPieOption(
  slices: PieData['slices'],
  opts: { donut?: boolean } = {},
): ECOption {
  return {
    color: PALETTE,
    tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
    legend: { type: 'scroll', orient: 'horizontal', bottom: 0 },
    series: [
      {
        type: 'pie',
        radius: opts.donut ? ['42%', '68%'] : '65%',
        center: ['50%', '46%'],
        data: slices,
        label: { formatter: '{b}: {d}%' },
        emphasis: { itemStyle: { shadowBlur: 10, shadowColor: 'rgba(0,0,0,0.4)' } },
      } as PieSeriesOption,
    ],
  };
}

export function buildCartesianOption(
  chartType: Exclude<ActiveChart, 'pie'>,
  data: CartesianData,
  rowCount: number,
): ECOption {
  const { categories, series } = data;
  const manyCategories = categories.length > ANGLE_THRESHOLD;

  const legend: LegendComponentOption = { type: 'scroll' };
  const gridBase: GridComponentOption = {
    containLabel: true,
    left: '3%',
    right: '4%',
    top: 40,
    bottom: manyCategories ? 80 : 40,
  };
  const gridHoriz: GridComponentOption = {
    containLabel: true,
    left: '3%',
    right: '4%',
    top: 40,
    bottom: 40,
  };

  const xAxisCategory = {
    type: 'category' as const,
    data: categories,
    axisLabel: {
      rotate: manyCategories ? 35 : 0,
      interval: manyCategories ? ('auto' as const) : 0,
      overflow: 'truncate' as const,
      width: 80,
    },
  };
  const yAxisCategory = {
    type: 'category' as const,
    data: categories,
    axisLabel: { overflow: 'truncate' as const, width: 140 },
  };

  const mkBarSeries = (stacked: boolean): BarSeriesOption[] =>
    series.map((s) => ({
      ...s,
      type: 'bar' as const,
      ...(stacked ? { stack: 'total' } : {}),
    }));

  const mkLineSeries = (): LineSeriesOption[] =>
    series.map((s) => ({
      ...s,
      type: 'line' as const,
      showSymbol: rowCount <= 50,
      smooth: false,
    }));

  switch (chartType) {
    case 'column':
      return {
        color: PALETTE,
        tooltip: { trigger: 'axis' },
        legend,
        grid: gridBase,
        xAxis: xAxisCategory,
        yAxis: { type: 'value' },
        series: mkBarSeries(false),
      };
    case 'bar':
      return {
        color: PALETTE,
        tooltip: { trigger: 'axis' },
        legend,
        grid: gridHoriz,
        xAxis: { type: 'value' },
        yAxis: yAxisCategory,
        series: mkBarSeries(false),
      };
    case 'line':
      return {
        color: PALETTE,
        tooltip: { trigger: 'axis' },
        legend,
        grid: gridBase,
        xAxis: xAxisCategory,
        yAxis: { type: 'value' },
        series: mkLineSeries(),
      };
    case 'stackedColumn':
      return {
        color: PALETTE,
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend,
        grid: gridBase,
        xAxis: xAxisCategory,
        yAxis: { type: 'value' },
        series: mkBarSeries(true),
      };
    case 'stackedBar':
      return {
        color: PALETTE,
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend,
        grid: gridHoriz,
        xAxis: { type: 'value' },
        yAxis: yAxisCategory,
        series: mkBarSeries(true),
      };
    default:
      return {};
  }
}
