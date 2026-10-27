import { MASK, PrivacyMode } from '../privacy';

import { BreakdownSeries } from './breakdown';

import { NetWorthDataPoint } from '@/apis/portfolioApis/types';
import { ECOption } from '@/charts/echarts';
import { escapeHtml } from '@/components/HtmlViewer/escapeHtml';
import { fmtCompact } from '@/utils/format';

// Some groups (e.g. Liability) are negative — fmtCompact already compares by magnitude, so a
// debt over 1M still gets the "-X.XM" treatment instead of a raw number.
const fmt = fmtCompact;

/**
 * The stacked area chart of the breakdown. Amounts show only in 'full'; 'hidden' shows each group
 * as a share of the day's total, 'changes' neither. `hiddenNames` are the series switched off in
 * the legend.
 */
export function buildNetWorthOption(
  points: NetWorthDataPoint[],
  breakdown: BreakdownSeries[],
  mode: PrivacyMode,
  hiddenNames: Set<string>,
): ECOption {
  const dates = points.map((p) => p.date);
  const totals = points.map((p) => p.totalTWD);
  return {
    tooltip: {
      trigger: 'axis',
      // Renders the tooltip as a document.body child instead of inside the chart's own container,
      // which otherwise clips it whenever it would overflow the card's edges (MUI Card/Paper clips
      // to its rounded corners via overflow:hidden) — happened on any side, worse with more series/
      // a taller legend squeezing the plot area.
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = params as {
          axisValue: string;
          seriesName: string;
          value: number;
          dataIndex: number;
        }[];
        const date = escapeHtml(p[0]?.axisValue ?? '');
        const dayTotal = totals[p[0]?.dataIndex ?? 0] || 0;
        const line = (series: { seriesName: string; value: number }) => {
          const s = { ...series, seriesName: escapeHtml(series.seriesName) };
          if (mode === 'full') return `${s.seriesName}：NT$${fmt(s.value)}`;
          // 'changes' hides both amounts and ratios; 'hidden' shows share-of-total instead.
          if (mode === 'changes') return `${s.seriesName}：${MASK}`;
          const pct = dayTotal > 0 ? (s.value / dayTotal) * 100 : 0;
          return `${s.seriesName}：${pct.toFixed(1)}%`;
        };
        // The stack already sums to the total, so no separate "總資產" series is rendered —
        // just surface the number (or masked value) in the tooltip for a quick read.
        const totalLine =
          mode === 'full'
            ? `<b>總資產：NT$${fmt(dayTotal)}</b>`
            : mode === 'changes'
              ? `<b>總資產：${MASK}</b>`
              : null;
        return [`<b>${date}</b>`, totalLine, ...p.map(line)].filter(Boolean).join('<br/>');
      },
    },
    // ECharts' own legend is hidden (see NetWorthLegend instead) — a scrolling single-row legend
    // needed pagination clicks to see every series, and a wrapped multi-row one pushed into/over
    // the xAxis labels since a fixed-height grid can't know how many rows the legend will take. A
    // separate DOM element below the chart just flex-wraps naturally with the page, no
    // chart-internal height math needed either way. The legend component still drives series
    // show/hide via `selected` even while hidden, so clicking the custom legend stays fully
    // interactive.
    legend: {
      show: false,
      selected: Object.fromEntries(breakdown.map((s) => [s.name, !hiddenNames.has(s.name)])),
    },
    grid: { top: 16, right: 16, bottom: 24, left: mode === 'full' ? 70 : 24 },
    xAxis: { type: 'category', data: dates, boundaryGap: false },
    yAxis: {
      type: 'value',
      // Mask the scale numbers unless fully visible; stack height/proportions stay accurate.
      axisLabel: { formatter: (v: number) => (mode === 'full' ? `${fmt(v)}` : '') },
    },
    series: breakdown.map((s) => ({
      name: s.name,
      type: 'line' as const,
      // Any group that ever goes negative (Liability under assetType/category grouping — or in
      // principle any group under currency grouping) gets its own stack, not the shared 'total'
      // one: ECharts stacks cumulatively, so a negative series sharing a stack with positive ones
      // renders as "gross assets, then a jump down" (its cumulative value = everything below it
      // + itself) instead of its own value dipping cleanly from 0. Its own stack group renders it
      // independently, from a 0 baseline down to its actual (negative) value.
      stack: s.values.some((v) => v < 0) ? `neg-${s.key}` : 'total',
      data: s.values,
      smooth: true,
      lineStyle: { width: 1.5, color: s.color },
      itemStyle: { color: s.color },
      areaStyle: { color: s.color, opacity: 0.65 },
      symbol: 'none' as const,
    })),
  };
}
