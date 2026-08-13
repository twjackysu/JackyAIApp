import { MASK, PrivacyMode } from './privacy';

import { ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';
import { fmtCompactTWD } from '@/utils/format';

export const MAX_ITEM_BARS = 7;
export const GAIN_COLOR = '#22c55e';
export const LOSS_COLOR = '#ef4444';
export const ANCHOR_COLOR = '#9CA3AF';
export const START_LABEL = '投入成本';
export const END_LABEL = '目前總值';

export interface ContributionItem {
  name: string;
  value: number;
}

/** Anchor bars (投入成本/目前總值) are absolute totals — masked outside 'full', matching every
 * other absolute figure on this page. Extracted as a pure function (rather than an inline arrow
 * in the component) specifically so it's unit-testable in isolation from React/privacy context —
 * PR #268 shipped this exact rule inverted (masked everything outside 'full' instead of just
 * anchors), see waterfallOption.test.ts. */
export function formatAnchorValue(mode: PrivacyMode, n: number): string {
  return mode === 'full' ? fmtCompactTWD(n) : MASK;
}

/** Delta bars are P&L changes — stay visible in 'changes' mode (that mode exists specifically to
 * show these), masked only in 'hidden'. */
export function formatDeltaValue(mode: PrivacyMode, n: number): string {
  return mode !== 'hidden' ? fmtCompactTWD(n) : MASK;
}

/** Axis ticks sit on a scale shared by both anchors and deltas, so showing them at all would leak
 * the anchors' absolute magnitude even in 'changes' mode — mask unless fully visible. */
export function formatAxisTickValue(mode: PrivacyMode, n: number): string {
  return mode === 'full' ? fmtCompactTWD(n) : '';
}

/** Standard two-time-point waterfall: leftmost bar = starting total, rightmost bar = ending
 * total (both neutral grey, full height from 0), middle bars = each holding's contribution
 * floating cumulatively between them (green = gain, red = loss). The ECharts trick behind the
 * floating bars: an invisible "base" series stacks under a visible "value" series so each middle
 * bar starts at its correct cumulative height instead of 0 — the two anchor bars opt out of that
 * (base always 0) since they're real totals, not deltas.
 *
 * Extracted from ContributionWaterfallChart.tsx as a pure function (no React, no privacy context)
 * specifically so the y-axis range math is unit-testable — this exact function has shipped two
 * axis-range bugs (see waterfallOption.test.ts) that a table-driven test over plain numbers would
 * have caught before either PR. */
export function buildWaterfallOption(
  items: ContributionItem[],
  startValue: number,
  // Anchor bars (投入成本/目前總值) are absolute totals — masked whenever `value()` would mask
  // them. Delta bars are P&L changes — masked whenever `change()` would mask them (i.e. only in
  // 'hidden' mode; still shown in 'changes' mode, which exists specifically to show these).
  formatAnchor: (n: number) => string,
  formatDelta: (n: number) => string,
  formatAxisTick: (n: number) => string,
): ECOption {
  const categories = [START_LABEL, ...items.map((i) => i.name), END_LABEL];
  const bases: number[] = [0];
  const values: number[] = [startValue];
  const colors: string[] = [ANCHOR_COLOR];

  let cumulative = startValue;
  for (const item of items) {
    const start = cumulative;
    cumulative += item.value;
    bases.push(Math.min(start, cumulative));
    values.push(Math.abs(item.value));
    colors.push(item.value >= 0 ? GAIN_COLOR : LOSS_COLOR);
  }

  bases.push(0);
  values.push(cumulative);
  colors.push(ANCHOR_COLOR);

  // Deliberately NOT zero-based: the anchor bars (投入成本/目前總值) are usually a much bigger
  // number than any single holding's contribution, so a 0-anchored axis squashes every middle
  // bar's height into a sliver near the top and the deltas become impossible to compare visually.
  // Zooming to the actual range the bars occupy (with a little padding) keeps the anchors' bottom
  // edge below the visible area — only their top edge (the value that matters) stays honest — while
  // making the delta bars' height differences actually readable. Critically, the anchor bars'
  // *base* (always 0 — see above) must be excluded from this range, or it drags dataMin straight
  // back down to 0 and reproduces the exact squashing this was meant to fix; only their top (the
  // real value, i.e. startValue/cumulative) is a meaningful bound.
  const middleEdges = items.flatMap((_, i) => [bases[i + 1], bases[i + 1] + values[i + 1]]);
  const dataMin = Math.min(...middleEdges, startValue, cumulative);
  const dataMax = Math.max(...middleEdges, startValue, cumulative);
  const padding = (dataMax - dataMin) * 0.1 || Math.abs(dataMax) * 0.1 || 1;
  const yMin = dataMin - padding;
  const yMax = dataMax + padding;

  return {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = (params as { axisValue: string; dataIndex: number }[])[0];
        if (!p) return '';
        const isAnchor = p.dataIndex === 0 || p.dataIndex === items.length + 1;
        if (isAnchor) return `<b>${p.axisValue}</b><br/>${formatAnchor(values[p.dataIndex])}`;
        const raw = items[p.dataIndex - 1].value;
        return `<b>${p.axisValue}</b><br/>${raw >= 0 ? '+' : ''}${formatDelta(raw)}`;
      },
    },
    grid: { top: 16, right: 16, bottom: 48, left: 56 },
    xAxis: {
      type: 'category',
      data: categories,
      axisLabel: { rotate: categories.length > 5 ? 30 : 0, fontSize: 11 },
    },
    yAxis: {
      type: 'value',
      min: yMin,
      max: yMax,
      axisLabel: { formatter: (v: number) => formatAxisTick(v) },
    },
    series: [
      {
        name: 'base',
        type: 'bar',
        stack: 'total',
        itemStyle: { color: 'transparent' },
        emphasis: { itemStyle: { color: 'transparent' } },
        data: bases,
        silent: true,
      },
      {
        name: '貢獻',
        type: 'bar',
        stack: 'total',
        data: values.map((v, i) => ({ value: v, itemStyle: { color: colors[i] } })),
        barWidth: '55%',
      },
    ],
  };
}
