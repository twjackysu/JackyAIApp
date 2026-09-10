import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

import { usePrivacy } from './privacy';
import { StatBlock } from './StatBlock';
import {
  buildWaterfallOption,
  ContributionItem,
  END_LABEL,
  formatAnchorValue,
  formatAxisTickValue,
  formatDeltaValue,
  MAX_ITEM_BARS,
  START_LABEL,
} from './waterfallOption';

import { PositionDto } from '@/apis/portfolioApis/types';
import { ECHARTS_THEME } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';
import { fmtCompactTWD, GAIN_COLOR, LOSS_COLOR, signPrefix } from '@/utils/format';

interface ContributionWaterfallChartProps {
  positions: PositionDto[];
}

/** Pure client-side arithmetic over PortfolioSummaryDto.positions — "which holdings actually
 * drove my return, between cost basis and today?" Contribution = unrealized P&L + cumulative
 * dividends per holding (both already on PositionDto); the walk starts at total cost basis and
 * ends at cost basis + total contribution. No backend call, no AI. Scoped to STOCK/FUND — cash/
 * liability/real estate carry no P&L concept. The smallest contributors beyond MAX_ITEM_BARS
 * collapse into one "其他" bucket so a portfolio with 30 holdings doesn't produce an unreadable
 * chart. */
export function ContributionWaterfallChart({ positions }: ContributionWaterfallChartProps) {
  const { mode, change } = usePrivacy();

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

  const option = buildWaterfallOption(
    items,
    startValue,
    (n) => formatAnchorValue(mode, n),
    (n) => formatDeltaValue(mode, n),
    (n) => formatAxisTickValue(mode, n),
  );

  return (
    <Box>
      {mode !== 'hidden' && (
        <Box sx={{ display: 'flex', gap: 3, mb: 2, flexWrap: 'wrap' }}>
          <StatBlock
            label="最大貢獻"
            value={`${topContributor.name} ${signPrefix(topContributor.value)}${change(fmtCompactTWD(topContributor.value))}`}
            color={GAIN_COLOR}
            valueVariant="body2"
          />
          {bottomContributor.value < 0 && bottomContributor !== topContributor && (
            <StatBlock
              label="最大拖累"
              value={`${bottomContributor.name} ${change(fmtCompactTWD(bottomContributor.value))}`}
              color={LOSS_COLOR}
              valueVariant="body2"
            />
          )}
        </Box>
      )}

      <ReactECharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 280 }} />

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
