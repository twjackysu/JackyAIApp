import { Lev2Holding, ScopeHoldings, StrategySymbol } from './drawdownBrake';

import { PositionDto } from '@/apis/portfolioApis/types';

/**
 * 「回落煞車」的策略範圍與分桶（需求 12 / DECISION-017·018·020·021）。
 *
 * 整個流動資產都計入整體曝險 E，但只對 0050、台股正 2 類（00631L、00685L）、現金類（合稱「核心」）
 * 給建議；其他持股（含 NTSD）一律「不動」。曝險一律用 DTO 的 `exposureFactor × marketValueTWD`
 * （與頁面右上角「水位」同一套倍數，後端 `PortfolioExposure` 算好），這裡不另寫倍數規則 — 否則卡片 E
 * 與水位會出現兩套算法。
 */

export interface ExcludedInstrument {
  market: string;
  symbol: string;
}

/** 被排除於策略範圍的標的。目前**沒有**：DECISION-021（取代 DECISION-018）把 NTSD 納入曝險，卡片 E 與
 * 右上角水位因此恆為同一個數字。機制（`isExcluded`、對帳行）保留，日後要排除某個標的只改這張表。 */
export const STRATEGY_EXCLUDED: ExcludedInstrument[] = [];

/**
 * 台股正 2 類（`lev2`）的成員 — **唯一**決定誰屬於這一類的地方（增減成員只改這張表）。00631L 與 00685L
 * 同性質（Boss 2026-10-11），合併計算加碼／減碼／不動。刻意不做「所有台股倍數 2 的商品自動歸類」：
 * 那會對沒回測過、Boss 沒點名的標的靜默給建議（DECISION-020 只對點名的標的給建議）。
 */
export const CORE_LEVERAGED_2X = ['00631L', '00685L'] as const;

export type Lev2Symbol = (typeof CORE_LEVERAGED_2X)[number];

/** 核心標的（會產生股數建議的標的）：0050 ＋ 台股正 2 類成員。 */
export const CORE_SYMBOLS: readonly StrategySymbol[] = ['0050', ...CORE_LEVERAGED_2X];

/** `composition` 的假設：0050 ＝ 1 倍、台股正 2 類成員 ＝ 2 倍（策略定義）。DTO 的倍數與此不符就是資料異常。 */
export const CORE_FACTORS: Record<StrategySymbol, number> = {
  '0050': 1,
  ...(Object.fromEntries(CORE_LEVERAGED_2X.map((symbol) => [symbol, 2])) as Record<
    Lev2Symbol,
    number
  >),
};

export type BucketKey = 'core0050' | 'coreLev2' | 'otherOne' | 'otherMulti' | 'cash';

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
  /** 台股正 2 類（00631L＋00685L）合計。 */
  coreLev2: BucketTotals;
  /** 正 2 類各檔的持有明細（同一標的多列已加總；沒持有的檔不列），依 `CORE_LEVERAGED_2X` 的順序。 */
  lev2Holdings: Lev2Holding[];
  /** 其他一倍商品（倍數 1，0050 除外）。 */
  otherOne: BucketTotals;
  /** 其他倍數商品（倍數 ≠ 0、1 且不是核心標的，含反向、NTSD）。 */
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
  /** 被排除的持股。不計入任何桶；`STRATEGY_EXCLUDED` 為空時恆為空。 */
  excluded: PositionDto[];
  /** 被排除持股的市值合計（TWD），只供對帳行顯示。 */
  excludedValue: number;
  /** 範圍內價格取得失敗的持股（價值會被算成 0，A 與 E 都會被扭曲）。 */
  priceFailed: PositionDto[];
  /** 核心標的的倍數與策略定義不符者；非 null 時不給建議。 */
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

export function isExcluded(
  position: PositionDto,
  excluded: ExcludedInstrument[] = STRATEGY_EXCLUDED,
): boolean {
  return excluded.some((x) => x.market === position.market && x.symbol === position.symbol);
}

function coreSymbolOf(position: PositionDto): StrategySymbol | null {
  if (position.assetType !== 'STOCK' || position.market !== 'TW') return null;
  return CORE_SYMBOLS.find((s) => s === position.symbol) ?? null;
}

function isLev2Symbol(symbol: StrategySymbol): symbol is Lev2Symbol {
  return (CORE_LEVERAGED_2X as readonly string[]).includes(symbol);
}

/**
 * 持股 → 分桶。依序判定：不動產／負債（不在流動資產）略過 → 被排除 → 核心標的（0050、台股正 2 類）→
 * 倍數 0（現金類）→ 倍數 1（其他一倍）→ 其餘（其他倍數，含反向、NTSD）。
 *
 * 數量為 0 的列（`quantity <= 0`，非現金）不算持股，不佔列、也不算價格失敗。
 *
 * `excluded` 預設為 `STRATEGY_EXCLUDED`（目前是空表）；測試以假的排除表驗證對帳行機制。
 */
export function classifyPositions(
  positions: PositionDto[],
  excluded: ExcludedInstrument[] = STRATEGY_EXCLUDED,
): ScopeClassification {
  const result: ScopeClassification = {
    core0050: emptyBucket(),
    coreLev2: emptyBucket(),
    lev2Holdings: [],
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
  const lev2BySymbol = new Map<Lev2Symbol, Lev2Holding>();

  for (const p of positions) {
    if (p.assetType === 'REAL_ESTATE' || p.assetType === 'LIABILITY') continue;
    if (p.assetType !== 'CASH' && !(p.quantity > 0)) continue;

    if (isExcluded(p, excluded)) {
      result.excluded.push(p);
      result.excludedValue += p.marketValueTWD;
      continue;
    }

    if (p.priceFetchFailed) result.priceFailed.push(p);

    const core = coreSymbolOf(p);
    if (core) {
      if (p.exposureFactor !== CORE_FACTORS[core]) result.coreFactorAnomaly ??= core;
      if (isLev2Symbol(core)) {
        add(result.coreLev2, p);
        const held = lev2BySymbol.get(core) ?? { symbol: core, value: 0, shares: 0 };
        held.value += p.marketValueTWD;
        held.shares += p.quantity;
        lev2BySymbol.set(core, held);
      } else {
        add(result.core0050, p);
      }
    } else if (p.exposureFactor === 0) {
      add(result.cash, p);
    } else if (p.exposureFactor === 1) {
      add(result.otherOne, p);
    } else {
      add(result.otherMulti, p);
    }
  }

  result.lev2Holdings = CORE_LEVERAGED_2X.flatMap((symbol) => lev2BySymbol.get(symbol) ?? []);
  result.othersValue = result.otherOne.value + result.otherMulti.value;
  result.othersExposure = result.otherOne.exposure + result.otherMulti.exposure;
  const buckets = [
    result.core0050,
    result.coreLev2,
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
    lev2: c.coreLev2.value,
    cash: c.cash.value,
    othersValue: c.othersValue,
    othersExposure: c.othersExposure,
    lev2Holdings: c.lev2Holdings,
  };
}
