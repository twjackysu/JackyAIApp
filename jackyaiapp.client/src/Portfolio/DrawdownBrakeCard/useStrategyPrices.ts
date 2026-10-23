import { StrategyPrices, StrategySymbol } from '../drawdownBrake';
import { requiredPriceSymbols, StrategyPosition } from '../drawdownBrakeHoldings';

import { useLookupStockQuery } from '@/apis/portfolioApis';

function isValidPrice(price: number | null | undefined): price is number {
  return typeof price === 'number' && Number.isFinite(price) && price > 0;
}

/** 持有且快照有價格 → 用快照價（已含交易時段的即時價）；否則（沒持有，或價格抓取失敗）查價。
 * 沒持有 0050 / 00631L 的使用者（PM 假設 A4）也要有價格才能換算建議股數。`required` 為 false（這檔
 * 不會用到，見 `requiredPriceSymbols`）時不查價。 */
function useStrategyPrice(symbol: StrategySymbol, summaryPrice: number | null, required: boolean) {
  const needLookup = required && summaryPrice == null;
  const { data, isLoading } = useLookupStockQuery({ market: 'TW', symbol }, { skip: !needLookup });
  const lookedUp = data?.data.currentPrice;
  const price = summaryPrice ?? (isValidPrice(lookedUp) ? lookedUp : null);
  return { price, loading: needLookup && isLoading };
}

export interface StrategyPriceState {
  /** 算股數用得上的標的中，已有價格的那些。 */
  prices: StrategyPrices;
  /** 還有用得上的標的在查價。 */
  loading: boolean;
  /** 第一檔查完了卻沒有價格的標的。 */
  missing: StrategySymbol | undefined;
}

/** 建議股數要用的價格。只有算得到股數用得上的標的才需要價格（見 `requiredPriceSymbols`）。 */
export function useStrategyPrices(held: StrategyPosition[]): StrategyPriceState {
  const heldBySymbol = Object.fromEntries(held.map((h) => [h.symbol, h])) as Partial<
    Record<StrategySymbol, StrategyPosition>
  >;
  // hooks 不能放進迴圈，所以每個核心標的各呼叫一次；`Record<StrategySymbol, …>` 讓日後在
  // `CORE_LEVERAGED_2X` 增加成員時這裡編譯失敗。
  const required = requiredPriceSymbols(held);
  const priceInfo: Record<StrategySymbol, ReturnType<typeof useStrategyPrice>> = {
    '0050': useStrategyPrice(
      '0050',
      heldBySymbol['0050']?.price ?? null,
      required.includes('0050'),
    ),
    '00631L': useStrategyPrice(
      '00631L',
      heldBySymbol['00631L']?.price ?? null,
      required.includes('00631L'),
    ),
    '00685L': useStrategyPrice(
      '00685L',
      heldBySymbol['00685L']?.price ?? null,
      required.includes('00685L'),
    ),
  };

  const loading = required.some((s) => priceInfo[s].loading);
  const missing = required.find((s) => priceInfo[s].price == null && !priceInfo[s].loading);
  const prices: StrategyPrices = Object.fromEntries(
    required.flatMap((s) => {
      const price = priceInfo[s].price;
      return price == null ? [] : [[s, price]];
    }),
  );
  return { prices, loading, missing };
}
