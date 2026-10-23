import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

import { extractStrategyPositions } from '../drawdownBrakeHoldings';
import { CORE_SYMBOLS } from '../drawdownBrakeScope';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { fmtQty } from '@/utils/format';

/** 持股資料來源與日期 — 建議股數是依這份快照算的，使用者要能看出它有多新。 */
export function HoldingsInfo({ summary }: { summary: PortfolioSummaryDto }) {
  const held = extractStrategyPositions(summary.positions);
  const bySymbol = Object.fromEntries(held.map((h) => [h.symbol, h]));
  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="body2" sx={{ fontWeight: 600 }}>
        持股資料日期：{summary.effectiveDate ?? '—'}
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
        {CORE_SYMBOLS.map(
          (symbol) =>
            `${symbol} ${bySymbol[symbol] ? `${fmtQty(bySymbol[symbol].quantity)} 股` : '未持有'}`,
        ).join('、')}
        （取自持股快照，建議股數依此計算）
      </Typography>
    </Box>
  );
}
