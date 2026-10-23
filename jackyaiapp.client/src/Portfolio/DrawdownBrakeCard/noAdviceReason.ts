import { StrategySymbol } from '../drawdownBrake';
import { SplitGuardHit } from '../drawdownBrakeHoldings';
import { ScopeClassification } from '../drawdownBrakeScope';

/** 暫時不給建議的原因。 */
export type NoAdviceReason =
  | { kind: 'splitGuard'; hit: SplitGuardHit }
  | { kind: 'noScopeAssets' }
  /** 範圍內的持股暫時不能用：價格取得失敗的、核心倍數異常的，有就列。 */
  | {
      kind: 'unusableHoldings';
      priceFailed: ScopeClassification['priceFailed'];
      coreFactorAnomaly: ScopeClassification['coreFactorAnomaly'];
    }
  | { kind: 'priceLoading' }
  | { kind: 'priceMissing'; symbol: StrategySymbol };

interface NoAdviceInput {
  guardHit: SplitGuardHit | null;
  scope: ScopeClassification;
  priceLoading: boolean;
  missingPrice: StrategySymbol | undefined;
}

/** 依序擋下：分割疑慮 → 沒有流動資產 → 範圍內有價格失敗的持股、核心倍數異常（兩者都列）→ 查價中 →
 * 查不到價。回第一個成立的原因，畫面只說那一個；null 表示可以算建議。寧可暫時沒有建議，也不給錯的股數。 */
export function noAdviceReason({
  guardHit,
  scope,
  priceLoading,
  missingPrice,
}: NoAdviceInput): NoAdviceReason | null {
  if (guardHit) return { kind: 'splitGuard', hit: guardHit };
  if (!(scope.scopeAssets > 0)) return { kind: 'noScopeAssets' };
  if (scope.priceFailed.length > 0 || scope.coreFactorAnomaly != null) {
    return {
      kind: 'unusableHoldings',
      priceFailed: scope.priceFailed,
      coreFactorAnomaly: scope.coreFactorAnomaly,
    };
  }
  if (priceLoading) return { kind: 'priceLoading' };
  if (missingPrice) return { kind: 'priceMissing', symbol: missingPrice };
  return null;
}
