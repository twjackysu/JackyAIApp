import Typography from '@mui/material/Typography';

import { PRICE_HISTORY_DAYS } from './dayRanges';

/**
 * Says so when the chosen range reaches further back than the price history the server keeps.
 *
 * Stock prices are fetched for about six months, so for a longer range the days before that have
 * no price for any held stock, and the drawdown and benchmark curves start where prices do — the
 * chart was quietly showing about six months under a "1Y" or "3Y" button. Goes away when the
 * server can backfill longer history.
 */
export function HistoryLimitNote({ days }: { days: number }) {
  if (days <= PRICE_HISTORY_DAYS) return null;
  return (
    <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
      目前只保留約 6 個月的股價歷史，選更長的區間時，更早的日子沒有價格，圖會從約 6 個月前開始。
    </Typography>
  );
}
