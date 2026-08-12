import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';

import { usePrivacy } from './privacy';

import { PositionDto } from '@/apis/portfolioApis/types';
import { ECHARTS_THEME, ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

interface ContributionWaterfallChartProps {
  positions: PositionDto[];
}

const MAX_BARS = 8;
const GAIN_COLOR = '#22c55e';
const LOSS_COLOR = '#ef4444';
const TOTAL_COLOR = '#3B82F6';
const OTHER_COLOR = '#9CA3AF';

interface ContributionItem {
  name: string;
  value: number;
}

/** Standard ECharts waterfall trick: an invisible "base" series stacks under a visible "value"
 * series, so each bar floats at its correct cumulative height instead of starting from 0. */
function buildWaterfallOption(items: ContributionItem[], total: number): ECOption {
  const categories = [...items.map((i) => i.name), '總計'];
  const bases: number[] = [];
  const values: number[] = [];
  const colors: string[] = [];

  let cumulative = 0;
  for (const item of items) {
    const start = cumulative;
    cumulative += item.value;
    bases.push(Math.min(start, cumulative));
    values.push(Math.abs(item.value));
    // "其他" is a mixed bucket (winners and losers netted together) — its own gain/loss color
    // would misleadingly imply a single clean direction, so it gets a neutral color instead.
    colors.push(item.name === '其他' ? OTHER_COLOR : item.value >= 0 ? GAIN_COLOR : LOSS_COLOR);
  }
  // Total bar: floats from 0 (or from the bottom if the total is negative).
  bases.push(Math.min(0, total));
  values.push(Math.abs(total));
  colors.push(TOTAL_COLOR);

  return {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = (params as { axisValue: string; dataIndex: number }[])[0];
        if (!p) return '';
        const isTotal = p.dataIndex === items.length;
        const raw = isTotal ? total : items[p.dataIndex].value;
        return `<b>${p.axisValue}</b><br/>${raw >= 0 ? '+' : ''}NT$${raw.toLocaleString('zh-TW')}`;
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
      axisLabel: {
        formatter: (v: number) =>
          Math.abs(v) >= 1_000_000 ? `${(v / 1_000_000).toFixed(1)}M` : `${v}`,
      },
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
 * drove my return?" Contribution = unrealized P&L + cumulative dividends per holding (both already
 * on PositionDto). No backend call, no AI. Scoped to STOCK/FUND — cash/liability/real estate carry
 * no P&L concept. The smallest contributors beyond MAX_BARS collapse into one "其他" bucket so a
 * portfolio with 30 holdings doesn't produce an unreadable chart. */
export function ContributionWaterfallChart({ positions }: ContributionWaterfallChartProps) {
  const { showRatio } = usePrivacy();

  const contributors = positions
    .filter((p) => p.assetType === 'STOCK' || p.assetType === 'FUND')
    .map((p) => ({
      name: p.name || p.symbol,
      value: (p.unrealizedPnlTWD ?? 0) + p.dividendsTWD,
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

  const total = contributors.reduce((sum, c) => sum + c.value, 0);

  let items = contributors;
  if (contributors.length > MAX_BARS) {
    const kept = contributors.slice(0, MAX_BARS - 1);
    const restSum = contributors.slice(MAX_BARS - 1).reduce((sum, c) => sum + c.value, 0);
    items = [...kept, { name: '其他', value: restSum }];
  }

  const topContributor = contributors[0];
  const bottomContributor = contributors[contributors.length - 1];

  const option = buildWaterfallOption(items, total);

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
              NT${topContributor.value.toLocaleString('zh-TW')}
            </Typography>
          </Box>
          {bottomContributor.value < 0 && bottomContributor !== topContributor && (
            <Box>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                最大拖累
              </Typography>
              <Typography variant="body2" sx={{ fontWeight: 700, color: LOSS_COLOR }}>
                {bottomContributor.name} NT${bottomContributor.value.toLocaleString('zh-TW')}
              </Typography>
            </Box>
          )}
        </Box>
      )}

      <ReactEcharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 280 }} />

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
        貢獻 = 未實現損益（已填成本部分）+ 累計股息，僅計算股票與基金持倉
        {items.some((i) => i.name === '其他') ? `（前 ${MAX_BARS - 1} 大以外歸入「其他」）` : ''}。
      </Typography>
    </Box>
  );
}
