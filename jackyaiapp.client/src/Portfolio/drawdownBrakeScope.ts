import { ScopeHoldings, StrategySymbol } from './drawdownBrake';

import { PositionDto } from '@/apis/portfolioApis/types';

/**
 * 「回落煞車」的策略範圍與分桶（需求 12 / DECISION-017·018·020）。
 *
 * 整個流動資產都計入整體曝險 E，但只對 0050、00631L、現金類（合稱「核心」）給建議；其他持股一律
 * 「不動」。曝險一律用 DTO 的 `exposureFactor × marketValueTWD`（與頁面右上角「水位」同一套倍數，
 * 後端 `PortfolioExposure` 算好），這裡不另寫倍數規則 — 否則卡片 E 與水位會出現兩套算法。
 */

export interface ExcludedInstrument {
  market: string;
  symbol: string;
}

/** 被排除於策略範圍的標的（DECISION-018：NTSD 屬於全球配置，與台股大盤訊號無直接對應）。
 * 要增減被排除的標的只改這張表，這是唯一決定「誰被排除」的地方。 */
export const STRATEGY_EXCLUDED: ExcludedInstrument[] = [{ market: 'US', symbol: 'NTSD' }];

/** `composition` 的假設：0050 ＝ 1 倍、00631L ＝ 2 倍（策略定義）。DTO 的倍數與此不符就是資料異常。 */
export const CORE_FACTORS: Record<StrategySymbol, number> = { '0050': 1, '00631L': 2 };

const CORE_SYMBOLS: StrategySymbol[] = ['0050', '00631L'];

export type BucketKey = 'core0050' | 'core00631L' | 'otherOne' | 'otherMulti' | 'cash';

export interface BucketTotals {
  /** 市值（TWD）。 */
  value: number;
  /** Σ(市值 × 曝險倍數)（TWD）；現金類恆為 0，反向商品為負。 */
  exposure: number;
  /** 這個桶有幾筆持股（0 表示沒有，UI 不顯示空列）。 */
  count: number;
}

export interface ScopeClassification {
  /** 0050。 */
  core0050: BucketTotals;
  /** 00631L。 */
  core00631L: BucketTotals;
  /** 其他一倍商品（倍數 1，0050 除外）。 */
  otherOne: BucketTotals;
  /** 其他倍數商品（倍數 ≠ 0、1 且不是 00631L，含反向）。 */
  otherMulti: BucketTotals;
  /** 現金類：全部現金（含外幣）加債券／貨幣型（倍數 0）。 */
  cash: BucketTotals;
  /** 其他持股（其他一倍＋其他倍數）的市值合計。 */
  othersValue: number;
  /** 其他持股的曝險 Σ(市值 × 倍數)，可為負。 */
  othersExposure: number;
  /** 策略範圍資產 A ＝ 五個桶的市值合計（＝流動總資產 − 被排除持股）。 */
  scopeAssets: number;
  /** 範圍內整體曝險 Σ(市值 × 倍數)。 */
  scopeExposure: number;
  /** 被排除的持股（NTSD）。不計入任何桶。 */
  excluded: PositionDto[];
  /** 被排除持股的市值合計（TWD），只供對帳行顯示。 */
  excludedValue: number;
  /** 範圍內價格取得失敗的持股（價值會被算成 0，A 與 E 都會被扭曲）。 */
  priceFailed: PositionDto[];
  /** 0050／00631L 的倍數與策略定義不符的標的；非 null 時不給建議。 */
  coreFactorAnomaly: StrategySymbol | null;
}

function emptyBucket(): BucketTotals {
  return { value: 0, exposure: 0, count: 0 };
}

function add(bucket: BucketTotals, position: PositionDto) {
  bucket.value += position.marketValueTWD;
  bucket.exposure += position.marketValueTWD * position.exposureFactor;
  bucket.count += 1;
}

export function isExcluded(position: PositionDto): boolean {
  return STRATEGY_EXCLUDED.some(
    (x) => x.market === position.market && x.symbol === position.symbol,
  );
}

function coreSymbolOf(position: PositionDto): StrategySymbol | null {
  if (position.assetType !== 'STOCK' || position.market !== 'TW') return null;
  return CORE_SYMBOLS.find((s) => s === position.symbol) ?? null;
}

/**
 * 持股 → 分桶。依序判定：不動產／負債（不在流動資產）略過 → 被排除 → 0050／00631L（核心）→
 * 倍數 0（現金類）→ 倍數 1（其他一倍）→ 其餘（其他倍數，含反向）。
 *
 * 數量為 0 的列（`quantity <= 0`，非現金）不算持股，不佔列、也不算價格失敗。
 */
export function classifyPositions(positions: PositionDto[]): ScopeClassification {
  const result: ScopeClassification = {
    core0050: emptyBucket(),
    core00631L: emptyBucket(),
    otherOne: emptyBucket(),
    otherMulti: emptyBucket(),
    cash: emptyBucket(),
    othersValue: 0,
    othersExposure: 0,
    scopeAssets: 0,
    scopeExposure: 0,
    excluded: [],
    excludedValue: 0,
    priceFailed: [],
    coreFactorAnomaly: null,
  };

  for (const p of positions) {
    if (p.assetType === 'REAL_ESTATE' || p.assetType === 'LIABILITY') continue;
    if (p.assetType !== 'CASH' && !(p.quantity > 0)) continue;

    if (isExcluded(p)) {
      result.excluded.push(p);
      result.excludedValue += p.marketValueTWD;
      continue;
    }

    if (p.priceFetchFailed) result.priceFailed.push(p);

    const core = coreSymbolOf(p);
    if (core) {
      if (p.exposureFactor !== CORE_FACTORS[core]) result.coreFactorAnomaly ??= core;
      add(core === '0050' ? result.core0050 : result.core00631L, p);
    } else if (p.exposureFactor === 0) {
      add(result.cash, p);
    } else if (p.exposureFactor === 1) {
      add(result.otherOne, p);
    } else {
      add(result.otherMulti, p);
    }
  }

  result.othersValue = result.otherOne.value + result.otherMulti.value;
  result.othersExposure = result.otherOne.exposure + result.otherMulti.exposure;
  const buckets = [
    result.core0050,
    result.core00631L,
    result.otherOne,
    result.otherMulti,
    result.cash,
  ];
  result.scopeAssets = buckets.reduce((sum, b) => sum + b.value, 0);
  result.scopeExposure = buckets.reduce((sum, b) => sum + b.exposure, 0);
  return result;
}

/** 分桶結果 → `advise` 的輸入。 */
export function toScopeHoldings(c: ScopeClassification): ScopeHoldings {
  return {
    '0050': c.core0050.value,
    '00631L': c.core00631L.value,
    cash: c.cash.value,
    othersValue: c.othersValue,
    othersExposure: c.othersExposure,
  };
}
