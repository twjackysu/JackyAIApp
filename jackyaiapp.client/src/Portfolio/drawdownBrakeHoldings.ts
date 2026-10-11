import { DEFAULT_LEV2_BUY, StrategySymbol } from './drawdownBrake';
import { CORE_LEVERAGED_2X, CORE_SYMBOLS } from './drawdownBrakeScope';

import { PositionDto } from '@/apis/portfolioApis/types';

/**
 * 「回落煞車」用到的持股資料，以及資料可信度的防護（PM 的 R2）。
 *
 * Portfolio 本身完全不處理股票分割：股數是使用者輸入的，市值 = 股數 × 最新價。分割前建立的快照
 * 會讓分割股的市值被嚴重低估，建議股數隨之大錯 — 所以這裡對 0050 / 00631L 的分割做硬性防護。
 * 00685L 沒有條目：Boss 2026-10-11 確認它沒有做過分割（上市未久）。
 * Portfolio 其他地方的同一問題是既有限制，不在這個功能的範圍。
 */

export interface SplitEvent {
  /** 分割（生效）日 yyyy-MM-dd */
  date: string;
  /** 1 拆 N */
  ratio: number;
}

/** 來源：Boss（Issue #431 §6），PM 未獨立驗證。新的分割發生時須改這張表。核心標的可以沒有條目
 * （沒做過分割）；沒有條目的標的不擋。
 * 00685L：Boss 2026-10-11 確認無分割；日後若分割，在此加一行。 */
export const SPLIT_EVENTS: Partial<Record<StrategySymbol, SplitEvent>> = {
  '0050': { date: '2025-06-18', ratio: 4 },
  '00631L': { date: '2026-03-31', ratio: 22 },
};

/** 快照距今超過這麼多天，提示持股資料可能已過期。7 天是 PM 的假設（待 Boss 確認），集中於此以便調整。 */
export const STALE_SNAPSHOT_DAYS = 7;

export interface StrategyPosition {
  symbol: StrategySymbol;
  quantity: number;
  /** 取自持股快照的價格；價格抓取失敗或不是正數時為 null（改用查價結果）。 */
  price: number | null;
}

/** 取出台股核心標的（0050、00631L、00685L）的持股（同一標的的多列加總）。沒持有的標的不會出現在結果裡。 */
export function extractStrategyPositions(positions: PositionDto[]): StrategyPosition[] {
  const result: StrategyPosition[] = [];
  for (const symbol of CORE_SYMBOLS) {
    const rows = positions.filter(
      (p) => p.assetType === 'STOCK' && p.market === 'TW' && p.symbol === symbol && p.quantity > 0,
    );
    if (rows.length === 0) continue;
    const quantity = rows.reduce((sum, p) => sum + p.quantity, 0);
    const priced = rows.find(
      (p) => !p.priceFetchFailed && Number.isFinite(p.currentPrice) && p.currentPrice > 0,
    );
    result.push({ symbol, quantity, price: priced ? priced.currentPrice : null });
  }
  return result;
}

export interface SplitGuardHit {
  symbol: StrategySymbol;
  event: SplitEvent;
}

/**
 * 持有分割標的、但持股快照早於它的分割日 → 股數可能還是分割前的，不可據此算買賣股數。
 *
 * - 沒持有該標的 → 不因它而擋（只有策略現金的使用者仍可看建議）。
 * - 快照日等於分割日不算「早於」。
 * - 持有分割標的卻沒有快照日期 → 無法確認，保守地視為可能過期而擋下（`effectiveDate` 為空時
 *   實務上不會有持股，這個分支只是不讓「查無日期」變成放行）。
 */
export function findSplitGuardHit(
  held: StrategyPosition[],
  effectiveDate: string | null | undefined,
): SplitGuardHit | null {
  for (const { symbol } of held) {
    const event = SPLIT_EVENTS[symbol];
    if (!event) continue;
    if (!effectiveDate || effectiveDate < event.date) return { symbol, event };
  }
  return null;
}

/**
 * 算建議股數**必須有價格**的核心標的：
 * - 0050：買賣都可能用到，沒持有時也要查價（PM 假設 A4）；
 * - 持有的台股正 2 成員：賣出與依持有比例加碼都要換算股數；
 * - 台股正 2 一檔都沒持有時只有 00631L（預設買進標的）；只持有 00685L 時 00631L 不需要價格。
 */
export function requiredPriceSymbols(held: StrategyPosition[]): StrategySymbol[] {
  const heldSymbols = new Set(held.map((h) => h.symbol));
  const anyLev2Held = CORE_LEVERAGED_2X.some((symbol) => heldSymbols.has(symbol));
  return CORE_SYMBOLS.filter((symbol) => {
    if (symbol === '0050' || heldSymbols.has(symbol)) return true;
    return !anyLev2Held && symbol === DEFAULT_LEV2_BUY;
  });
}

/** 重新整理時，範圍內價格取得失敗的持股最多多重抓幾檔（避免一次對報價來源發出太多請求）。 */
export const MAX_FAILED_PRICE_REFRESH = 5;

/**
 * 「重新整理資料」要重抓報價的代號：持有的核心標的，加上範圍內價格取得失敗的股票（例如 NTSD — 失敗
 * 會暫停整張卡片的建議，使用者唯一的手段就是重新取價）。`refresh-price` 只處理 STOCK（基金是靜默
 * no-op），所以基金不列；同一代號只列一次；失敗者最多 `MAX_FAILED_PRICE_REFRESH` 檔。
 */
export function priceRefreshSymbols(
  held: StrategyPosition[],
  priceFailed: PositionDto[],
): string[] {
  const symbols: string[] = held.map((h) => h.symbol);
  const failed = new Set(priceFailed.filter((p) => p.assetType === 'STOCK').map((p) => p.symbol));
  const extra = [...failed].filter((symbol) => !symbols.includes(symbol));
  return [...symbols, ...extra.slice(0, MAX_FAILED_PRICE_REFRESH)];
}

/** yyyy-MM-dd 之間相差的天數。以 UTC 解析兩端，不受時區與夏令時間影響。 */
export function daysBetween(fromISO: string, toISO: string): number {
  const from = Date.parse(`${fromISO}T00:00:00Z`);
  const to = Date.parse(`${toISO}T00:00:00Z`);
  return Math.round((to - from) / 86_400_000);
}

/** 快照距 `todayISO` 超過 `STALE_SNAPSHOT_DAYS` 天。沒有日期（無法判斷）不算過期。 */
export function isSnapshotStale(
  effectiveDate: string | null | undefined,
  todayISO: string,
): boolean {
  if (!effectiveDate) return false;
  return daysBetween(effectiveDate, todayISO) > STALE_SNAPSHOT_DAYS;
}

export function splitGuardMessage({ symbol, event }: SplitGuardHit): string {
  return `你的持股快照建立於 ${symbol} 分割（${event.date} 1 拆 ${event.ratio}）之前，股數可能未調整，請先更新持股`;
}

export const STALE_SNAPSHOT_MESSAGE = '持股資料可能已過期，請先更新持股';
