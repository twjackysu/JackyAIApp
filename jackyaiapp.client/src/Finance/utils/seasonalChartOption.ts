import { formatMonthDay, isAxisLabelShown, lineValuesOnAxis, lookupSample } from './seasonal';

import type { SeasonalLine, SeasonalResult } from './seasonal';
import type { ECOption } from '@/charts/echarts';

import { fmtNum } from '@/utils/format';

// ECharts option for the 過去 5 年同期間走勢 chart, kept out of the component so the series
// layout and tooltip wording can be unit-tested without a canvas.

/** The slice of an ECharts axis-tooltip param this module reads. */
export interface SeasonalTooltipParam {
  seriesName?: string;
  /** HTML for the coloured dot ECharts draws beside a series name. */
  marker?: unknown;
  axisValue?: string | number;
}

const MUTED = 'opacity:.7';
/** Spacing inside tooltip HTML (a plain space would collapse next to the marker). */
const GAP = '&nbsp;&nbsp;';

function tooltipRow(line: SeasonalLine, key: string, asOfKey: string, marker: string): string {
  const found = lookupSample(line, key, asOfKey);
  if (found.kind === 'no-data') return `${marker}${line.year}${GAP}—${GAP}尚無資料`;
  if (found.kind === 'not-started') return `${marker}${line.year}${GAP}—${GAP}尚未開盤`;

  const { sample } = found;
  // A sample dated before the hovered day means the market was shut that day: the previous
  // close stands in, and its own date is printed so it can't be read as that day's trade.
  const dateText = sample.key === key ? sample.date : `休市沿用 ${sample.date}`;
  return [
    `${marker}${line.year}${GAP}收盤 ${fmtNum(sample.close, 2)}`,
    // Each fragment is unbreakable: a narrow phone wraps between them, never inside a date or a number.
    `<span style="${MUTED};padding-left:14px"><span style="white-space:nowrap">${dateText}</span> <span style="white-space:nowrap">相對 ${sample.index.toFixed(1)}</span></span>`,
  ].join('<br/>');
}

/**
 * Tooltip HTML for the hovered day: per visible line, the real close and real date of the most
 * recent trading day not after it. `params` holds only the lines the legend currently shows.
 */
export function formatSeasonalTooltip(
  result: SeasonalResult,
  params: SeasonalTooltipParam[],
): string {
  const key = params[0]?.axisValue;
  if (typeof key !== 'string') return '';

  const rows = params.flatMap((p) => {
    const line = result.lines.find((l) => String(l.year) === p.seriesName);
    return line
      ? [tooltipRow(line, key, result.asOfKey, typeof p.marker === 'string' ? p.marker : '')]
      : [];
  });
  return [`<b>${formatMonthDay(key)}</b>`, ...rows].join('<br/>');
}

/**
 * One line series per year on a calendar-day category axis. A year's line only has values on days
 * the market was open and bridges the gaps between them, so a holiday never breaks it; the current
 * year simply ends at its last data day.
 */
export function buildSeasonalOption(
  result: SeasonalResult,
  colorOf: (line: SeasonalLine) => string,
): ECOption {
  return {
    animation: false,
    legend: {
      // Plain, not scroll: five short years fit a 360px chart on one row if the swatches are
      // compact, and anything wider wraps instead of hiding entries behind a pager.
      type: 'plain',
      top: 0,
      data: result.lines.map((l) => String(l.year)),
      itemWidth: 16,
      itemHeight: 8,
      itemGap: 10,
      textStyle: { fontSize: 12 },
    },
    grid: { top: 48, right: 12, bottom: 8, left: 8, containLabel: true },
    tooltip: {
      trigger: 'axis',
      confine: true,
      // Never wider than the chart, so a narrow phone wraps the text instead of clipping it.
      extraCssText: 'max-width: 90%; white-space: normal;',
      formatter: (params: unknown) =>
        formatSeasonalTooltip(
          result,
          (Array.isArray(params) ? params : [params]) as SeasonalTooltipParam[],
        ),
    },
    xAxis: {
      type: 'category',
      data: result.axis,
      boundaryGap: false,
      axisLabel: {
        interval: (index: number) => isAxisLabelShown(result.axis, index),
        formatter: (value: string) => formatMonthDay(value),
        hideOverlap: true,
      },
    },
    yAxis: {
      type: 'value',
      scale: true,
      axisLabel: { formatter: (v: number) => String(Math.round(v)) },
    },
    series: result.lines.map((line) => {
      const color = colorOf(line);
      return {
        name: String(line.year),
        type: 'line' as const,
        data: lineValuesOnAxis(line, result.axis),
        connectNulls: true,
        showSymbol: false,
        symbol: 'none' as const,
        // The current year is the one being compared against: heavier and drawn on top.
        z: line.isCurrent ? 3 : 2,
        lineStyle: { width: line.isCurrent ? 3 : 1.5, color },
        itemStyle: { color },
      };
    }),
  };
}
