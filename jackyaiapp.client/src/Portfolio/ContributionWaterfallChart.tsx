import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';

import { MASK, usePrivacy } from './privacy';

import { PositionDto } from '@/apis/portfolioApis/types';
import { ECHARTS_THEME, ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

interface ContributionWaterfallChartProps {
  positions: PositionDto[];
}

const MAX_ITEM_BARS = 7;
const GAIN_COLOR = '#22c55e';
const LOSS_COLOR = '#ef4444';
const ANCHOR_COLOR = '#9CA3AF';
const START_LABEL = '投入成本';
const END_LABEL = '目前總值';

interface ContributionItem {
  name: string;
  value: number;
}

/** Standard two-time-point waterfall: leftmost bar = starting total, rightmost bar = ending
 * total (both neutral grey, full height from 0), middle bars = each holding's contribution
 * floating cumulatively between them (green = gain, red = loss). The ECharts trick behind the
 * floating bars: an invisible "base" series stacks under a visible "value" series so each middle
 * bar starts at its correct cumulative height instead of 0 — the two anchor bars opt out of that
 * (base always 0) since they're real totals, not deltas. */
function buildWaterfallOption(
  items: ContributionItem[],
  startValue: number,
  formatValue: (n: number) => string,
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
        if (isAnchor) return `<b>${p.axisValue}</b><br/>${formatValue(values[p.dataIndex])}`;
        const raw = items[p.dataIndex - 1].value;
        return `<b>${p.axisValue}</b><br/>${raw >= 0 ? '+' : ''}${formatValue(raw)}`;
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
      axisLabel: { formatter: (v: number) => formatValue(v) },
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

/** Pure client-side arithmetic over PortfolioSummaryDto.positions — "which holdings actually
 * drove my return, between cost basis and today?" Contribution = unrealized P&L + cumulative
 * dividends per holding (both already on PositionDto); the walk starts at total cost basis and
 * ends at cost basis + total contribution. No backend call, no AI. Scoped to STOCK/FUND — cash/
 * liability/real estate carry no P&L concept. The smallest contributors beyond MAX_ITEM_BARS
 * collapse into one "其他" bucket so a portfolio with 30 holdings doesn't produce an unreadable
 * chart. */
export function ContributionWaterfallChart({ positions }: ContributionWaterfallChartProps) {
  const { mode, showRatio, value } = usePrivacy();

  const holdings = positions.filter((p) => p.assetType === 'STOCK' || p.assetType === 'FUND');
  const contributors = holdings
    .map((p) => ({
      name: p.name || p.symbol,
      value: (p.unrealizedPnlTWD ?? 0) + p.dividendsTWD,
      costBasis: p.costBasisTWD ?? 0,
    }))
    .filter((c) => c.value !== 0)
    .sort((a, b) => b.value - a.value);

  if (contributors.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        尚無已填成本或有股息紀錄的持倉，無法計算報酬貢獻度
      </Typography>
    );
  }

  const startValue = contributors.reduce((sum, c) => sum + c.costBasis, 0);

  let items: ContributionItem[] = contributors;
  if (contributors.length > MAX_ITEM_BARS) {
    const kept = contributors.slice(0, MAX_ITEM_BARS - 1);
    const restSum = contributors.slice(MAX_ITEM_BARS - 1).reduce((sum, c) => sum + c.value, 0);
    items = [...kept, { name: '其他', value: restSum }];
  }

  const topContributor = contributors[0];
  const bottomContributor = contributors[contributors.length - 1];

  const formatValue = (n: number) => {
    if (mode !== 'full') return MASK;
    return Math.abs(n) >= 1_000_000
      ? `${(n / 1_000_000).toFixed(1)}M`
      : `NT$${n.toLocaleString('zh-TW')}`;
  };

  const option = buildWaterfallOption(items, startValue, formatValue);

  return (
    <Box>
      {showRatio && (
        <Box sx={{ display: 'flex', gap: 3, mb: 2, flexWrap: 'wrap' }}>
          <Box>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
              最大貢獻
            </Typography>
            <Typography variant="body2" sx={{ fontWeight: 700, color: GAIN_COLOR }}>
              {topContributor.name} {topContributor.value >= 0 ? '+' : ''}
              {value(`NT$${topContributor.value.toLocaleString('zh-TW')}`)}
            </Typography>
          </Box>
          {bottomContributor.value < 0 && bottomContributor !== topContributor && (
            <Box>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                最大拖累
              </Typography>
              <Typography variant="body2" sx={{ fontWeight: 700, color: LOSS_COLOR }}>
                {bottomContributor.name}{' '}
                {value(`NT$${bottomContributor.value.toLocaleString('zh-TW')}`)}
              </Typography>
            </Box>
          )}
        </Box>
      )}

      <ReactEcharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 280 }} />

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
        {START_LABEL} → 各持倉貢獻（未實現損益＋累計股息）→ {END_LABEL}，僅計算股票與基金持倉
        {items.some((i) => i.name === '其他')
          ? `（前 ${MAX_ITEM_BARS - 1} 大以外歸入「其他」）`
          : ''}
        。
      </Typography>
    </Box>
  );
}
