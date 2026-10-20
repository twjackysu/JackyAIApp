import Typography from '@mui/material/Typography';

import { PRICE_HISTORY_DAYS } from './dayRanges';

import type { HistoryBackfill } from '@/apis/portfolioApis/types';

const DAY_MS = 86_400_000;

/**
 * How much later than the range start the first point may fall before the chart says where it
 * does start — a weekend, a holiday or a snapshot a few days into the range is not worth a note.
 */
const LATE_START_TOLERANCE_DAYS = 14;

/** Midnight UTC of a `yyyy-MM-dd` date, or NaN for anything else. */
function utcDay(iso: string): number {
  return /^\d{4}-\d{2}-\d{2}/.test(iso) ? Date.parse(`${iso.slice(0, 10)}T00:00:00Z`) : NaN;
}

function noteText(
  days: number,
  backfill: HistoryBackfill | null | undefined,
  firstPointDate: string | null | undefined,
): string | null {
  if (backfill?.state === 'Running') {
    return (
      `正在補齊更早的股價（已補到 ${backfill.coveredFrom}，目標 ${backfill.targetFrom}）。` +
      '圖與數字會隨補齊自動更新；補齊期間最大回撤、報酬等數字是暫定的，補齊後可能改變。'
    );
  }
  if (backfill?.state === 'Stalled') {
    return (
      `部分股票更早的股價暫時抓不到（已補到 ${backfill.coveredFrom}，目標 ${backfill.targetFrom}），` +
      '目前沒有在補。稍後重新整理頁面會再試。'
    );
  }

  // Complete, or nothing was fetched. The server keeps no older US prices, and a holding may
  // simply be newer than the range, so the chart can still start well after the range does.
  if (days <= PRICE_HISTORY_DAYS || !firstPointDate) return null;
  const first = utcDay(firstPointDate);
  const now = new Date();
  const rangeStart = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) - days * DAY_MS;
  if (Number.isNaN(first) || (first - rangeStart) / DAY_MS <= LATE_START_TOLERANCE_DAYS) {
    return null;
  }
  return `圖從 ${firstPointDate.slice(0, 10)} 開始——更早沒有持倉紀錄，或沒有價格。`;
}

/**
 * Says where a long-range chart's data stands: whether older prices are still being fetched in
 * the background (and so the numbers are provisional), couldn't be fetched, or — once nothing
 * more is coming — where the chart actually starts when that is well after the range does.
 *
 * Drawdown and benchmark comparison share it. `backfill` is the response's own status and
 * `firstPointDate` its first plotted day.
 */
export function HistoryLimitNote({
  days,
  backfill,
  firstPointDate,
}: {
  days: number;
  backfill?: HistoryBackfill | null;
  firstPointDate?: string | null;
}) {
  const text = noteText(days, backfill, firstPointDate);
  if (!text) return null;
  return (
    <Typography
      variant="caption"
      color="text.secondary"
      sx={{ display: 'block', mb: 1, overflowWrap: 'anywhere' }}
    >
      {text}
    </Typography>
  );
}
