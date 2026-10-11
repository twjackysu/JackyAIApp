import { Lev2Symbol } from './drawdownBrakeScope';

import { readJson, writeJson } from '@/utils/storage';

/**
 * 「回落煞車」(Issue #431 / DECISION-013) — 0050 + 台股正 2（00631L、00685L）+ 現金 的目標配置與加減碼建議。
 *
 * `composition`、`PROFILES`、`REBALANCE_BAND`、`roundHalfAwayFromZero` 與固定參數是 #431 §3、§4
 * 與其 Python 參考實作的逐字移植。規則與固定參數屬 Boss 的回測結論，不得調整，也不得新增選項
 * （防守 E ≥ 0.5、不煞車的固定槓桿、突破新高減碼、深跌提前加碼、階梯式加碼一律不做）。大盤訊號本身
 * （狀態機）由後端計算；這裡只負責「個人建議」。
 *
 * 需求 12（DECISION-017·020）把「整體曝險」的範圍擴大到整個流動資產：其他持股計入整體曝險，但
 * 建議只針對 0050、台股正 2 類、現金（核心）。`advise` 的算法見 ARCHITECTURE.md「回落煞車 — 以整個
 * 流動資產判斷曝險、分桶顯示」；其他持股為 0 時，結果與 #431 §7 的原算法逐項相同。
 *
 * 需求 12 v4.3（DECISION-021）把 00685L 併入 00631L 成「台股正 2 類」（`lev2`，倍數 2）：`composition`
 * 與 `advise` 的數學只認得類別合計；類別的金額變動由 `splitLev2Order` 分配到各檔（「買／賣哪一檔」
 * 唯一決定的地方）。
 */

export type BrakeState = 'OFFENSE' | 'DEFENSE';
/** 核心標的：0050 與台股正 2 類的成員（成員表見 `drawdownBrakeScope.CORE_LEVERAGED_2X`）。 */
export type StrategySymbol = '0050' | Lev2Symbol;
export type ProfileKey = 'conservative' | 'balanced' | 'aggressive';

/** E = 0050 權重 + 2 × 台股正 2 權重（有效大盤曝險）。 */
export interface BrakeParams {
  /** 進攻 E */
  eOn: number;
  /** 防守 E */
  eOff: number;
  /** 最低現金比例 k */
  k: number;
}

export interface BrakeProfile extends BrakeParams {
  label: string;
}

/** (進攻 E, 防守 E, k)，對應 #431 §2 / §7 的 PROFILES。 */
export const PROFILES: Record<ProfileKey, BrakeProfile> = {
  conservative: { label: '保守', eOn: 1.0, eOff: 0, k: 0.2 },
  balanced: { label: '平衡', eOn: 1.4, eOff: 0, k: 0.2 },
  aggressive: { label: '積極', eOn: 1.8, eOff: 0, k: 0.1 },
};

export const PROFILE_KEYS: ProfileKey[] = ['conservative', 'balanced', 'aggressive'];
export const DEFAULT_PROFILE: ProfileKey = 'balanced';

/** 防守 E 只有這兩個選項（#431 §2）。≥ 0.5 的選項回測最大回撤惡化到 -47% ~ -60%，刻意不提供。 */
export const DEFENSE_E_OPTIONS = [0, 0.3] as const;
export type DefenseE = (typeof DEFENSE_E_OPTIONS)[number];

/** 進階的「最低現金比例 k」可輸入範圍。 */
export const MIN_CASH_RATIO_MIN = 0;
export const MIN_CASH_RATIO_MAX = 0.5;

/** 曝險偏離超過此值才觸發再平衡（#431 §4）。 */
export const REBALANCE_BAND = 0.1;

/** 煞車觸發線（-8%）— 與後端 `DD_TRIGGER = 0.08` 相同的固定參數，這裡只用來由預估的回落幅度
 * 推算「距煞車觸發點」。不得調整（DECISION-013）。 */
export const DD_TRIGGER_PCT = 8;

/** 核心三桶的權重；`lev2` ＝ 台股正 2 類（00631L＋00685L）合計。 */
export interface Weights {
  '0050': number;
  lev2: number;
  cash: number;
}

export interface Composition {
  weights: Weights;
  /** 實際用來配置的 E；目標 E 不可行時為截斷後的值。 */
  effectiveE: number;
  /** 目標 E 超過 2 × (1 − k)，已被截斷（UI 須提示「現金比例過高，無法達到此曝險」）。 */
  truncated: boolean;
}

/** 目標曝險 E 與最低現金比例 k → 0050 / 台股正 2 / 現金 權重（#431 §3；原 00631L 的位置由正 2 類承擔）。 */
export function composition(E: number, k: number): Composition {
  const inv = 1 - k; // 最多可投入的比例
  if (E <= inv) {
    return {
      weights: { '0050': E, lev2: 0, cash: 1 - E },
      effectiveE: E,
      truncated: false,
    };
  }
  const truncatedE = Math.min(E, 2 * inv); // 不可行時截斷
  return {
    weights: { '0050': 2 * inv - truncatedE, lev2: truncatedE - inv, cash: k },
    effectiveE: truncatedE,
    truncated: E > 2 * inv,
  };
}

/** 四捨五入到整數，.5 一律遠離 0（-2.5 → -3、2.5 → 3）。不是 `Math.round`（對負數是往上捨入，
 * -2.5 → -2），也不是 Python 的銀行家捨入（2.5 → 2）。 */
export function roundHalfAwayFromZero(x: number): number {
  const rounded = Math.sign(x) * Math.round(Math.abs(x));
  return rounded === 0 ? 0 : rounded; // 避免 -0
}

/** 台股正 2 類的一檔持股（同一標的多列已加總）。 */
export interface Lev2Holding {
  symbol: Lev2Symbol;
  /** 市值（TWD）。 */
  value: number;
  /** 持有股數；賣出不得超過它。 */
  shares: number;
}

/**
 * 策略範圍內的持股（TWD）。核心三桶（0050、台股正 2 類、現金類）各自的市值，加上其他持股（其他一倍
 * 商品＋其他倍數商品，含 NTSD）的市值與曝險合計 — 其他持股只計入整體曝險，不產生建議。
 */
export interface ScopeHoldings {
  '0050': number;
  /** 台股正 2 類（00631L＋00685L）的市值合計。 */
  lev2: number;
  cash: number;
  /** 其他持股的市值合計。 */
  othersValue: number;
  /** 其他持股的曝險 Σ(市值 × 曝險倍數)，反向商品可為負。 */
  othersExposure: number;
  /**
   * 正 2 類各檔的持有明細，供 `splitLev2Order` 分配股數與限制賣出上限。省略時視為正 2 類全部持有在
   * 00631L、股數＝市值 ÷ 價格（退化情形；`toScopeHoldings` 一律會提供）。
   */
  lev2Holdings?: Lev2Holding[];
}

/** 核心標的的最新收盤價（TWD）。缺價格（沒給、≤ 0、非有限數）的標的無法換算股數，不產生 order。 */
export type StrategyPrices = Partial<Record<StrategySymbol, number>>;

export type AdviceAction = '加碼' | '減碼' | '不動';

export interface Order {
  symbol: StrategySymbol;
  /** 有號股數：< 0 賣出、> 0 買進（不為 0）。 */
  shares: number;
}

/** 核心三桶的目標市值（TWD）；`lev2` ＝ 台股正 2 類合計。 */
export type CoreTargets = Record<'0050' | 'lev2' | 'cash', number>;

export interface Advice {
  action: AdviceAction;
  /** 這次是否為狀態切換日（OFFENSE ↔ DEFENSE）。 */
  switched: boolean;
  /** A：策略範圍資產 ＝ 0050 + 台股正 2 + 現金類 + 其他持股；也是曝險的分母。 */
  scopeAssets: number;
  /** 目前整體有效曝險 E_act。 */
  eActual: number;
  /** 等級表的目標 E（進攻 `eOn`／防守 `eOff`），未經任何截斷。 */
  eTarget: number;
  /** 整體可達曝險 E_reach：核心在最低現金限制下能把整體曝險調到的值。 */
  eReach: number;
  /** 其他持股單獨的曝險就已高於目標（核心降到最低仍達不到）。 */
  aboveTarget: boolean;
  /** 目標在其他持股與最低現金限制下無法達到。 */
  capped: boolean;
  /** 核心各桶的目標市值（即使「不動」也回傳，供 UI 顯示）。 */
  targets: CoreTargets;
  /** 核心各桶目標占策略範圍資產 A 的比例（＝ targets ÷ A）。 */
  weights: Weights;
  /** 其他持股占策略範圍資產 A 的比例。 */
  othersShare: number;
  /** 先賣後買；各檔股數（正 2 類已分配到 00631L／00685L），股數為 0 的標的不列入。「不動」時為空。 */
  orders: Order[];
  /** 正 2 類的金額變動（TWD）＝ Σ(實際建議股數 × 價格)，> 0 買進、< 0 賣出、0 不動；供類別列顯示，與各檔股數一致。 */
  lev2Change: number;
  /** 現金類的金額變動（TWD）＝ −Σ(實際建議股數 × 價格)；用實際股數而非理想值，與列出的股數一致。 */
  cashChange: number;
}

/** 浮點比較的容差，相對於策略範圍資產 A（TWD）。 */
const FLOAT_TOLERANCE = 1e-6;

/** 正 2 類沒有任何持股、卻需要加碼時的預設買進標的：回測標的（PM 假設 A28）。 */
export const DEFAULT_LEV2_BUY: Lev2Symbol = '00631L';

function isValidPrice(price: number | undefined): price is number {
  return typeof price === 'number' && Number.isFinite(price) && price > 0;
}

/**
 * 台股正 2 類的金額變動 → 各檔的有號股數。**「買／賣哪一檔」只在這裡決定**；日後偏好改了（例如只買
 * 00685L）只改這個函式。
 *
 * - `change`（TWD）：類別目標市值 − 目前市值；> 0 加碼、< 0 減碼。
 * - 減碼：依各檔**現值比例**分攤賣出金額，每檔上限＝持有股數。
 * - 加碼：依各檔**持有現值比例**分攤；只持有一檔 → 全給那一檔；**都沒持有 → 全部買 00631L**。
 * - 各檔股數 `roundHalfAwayFromZero`，所以合計金額與 `change` 會差不到一股的價格；呼叫端用實際股數
 *   算現金變動。冪等性在類別層級成立（照做後再算 ＝ 不動）。
 * - 缺價格的標的不產生 order；結果不含 0 股，順序依 `holdings`（賣出與買進各自）。
 */
export function splitLev2Order(
  change: number,
  holdings: Lev2Holding[],
  prices: StrategyPrices,
): Order[] {
  if (!Number.isFinite(change) || change === 0) return [];
  const held = holdings.filter((h) => h.value > 0);
  const total = held.reduce((sum, h) => sum + h.value, 0);

  if (change > 0) {
    const targets =
      held.length > 0
        ? held.map((h) => ({ symbol: h.symbol, ratio: h.value / total }))
        : [{ symbol: DEFAULT_LEV2_BUY, ratio: 1 }];
    return targets.flatMap(({ symbol, ratio }) => {
      const price = prices[symbol];
      if (!isValidPrice(price)) return [];
      const shares = roundHalfAwayFromZero((change * ratio) / price);
      return shares > 0 ? [{ symbol, shares }] : [];
    });
  }

  return held.flatMap((h) => {
    const price = prices[h.symbol];
    if (!isValidPrice(price)) return [];
    const wanted = roundHalfAwayFromZero((-change * (h.value / total)) / price);
    const shares = Math.min(wanted, Math.floor(h.shares));
    return shares > 0 ? [{ symbol: h.symbol, shares: -shares }] : [];
  });
}

/** `ScopeHoldings.lev2Holdings` 省略時的退化情形：正 2 類全部持有在 00631L，股數 ＝ 市值 ÷ 價格。 */
function fallbackLev2Holdings(holdings: ScopeHoldings, prices: StrategyPrices): Lev2Holding[] {
  if (!(holdings.lev2 > 0)) return [];
  const price = prices[DEFAULT_LEV2_BUY];
  const shares = isValidPrice(price) ? Math.round(holdings.lev2 / price) : 0;
  return [{ symbol: DEFAULT_LEV2_BUY, value: holdings.lev2, shares }];
}

/**
 * 狀態與持股 → 加碼／減碼／不動（#431 §4 / §7 `advise`，需求 12c 擴及整個流動資產）。
 *
 * 符號：C ＝ 0050 + 台股正 2 + 現金類（核心可動用資金，內部調整、不引入外部資金）；A ＝ C + 其他持股
 * 市值；X_o ＝ 其他持股的曝險；E_t ＝ 等級表的目標 E；k ＝ 最低現金比例。
 *
 *   c_min  = min(C, k·A)                  最低現金，以 A 為基準
 *   I      = C − c_min                    核心可投入
 *   e*     = E_t·A − X_o                  核心必須提供的曝險
 *   e      = clamp(e*, 0, 2·I)            核心可達的曝險
 *   E_reach= (X_o + e) / A                整體可達曝險
 *
 * 核心目標依 `composition` 同一規則分段（e ≤ I 只用 0050；e > I 加台股正 2）。X_o 與其他持股市值為 0
 * 時，A ＝ C，結果與原本只看 0050 + 00631L + 現金的算法逐項相同。台股正 2 類（00631L＋00685L，倍數 2）
 * 只以類別合計參與計算，類別的金額變動再由 `splitLev2Order` 分配到各檔。
 *
 * 與參考實作的差異：
 * 1. `prevState` 為 null（序列第一天，沒有前一日）時視為初始狀態 OFFENSE，與狀態機的起始值一致。
 * 2. 偏離拿「可達 E」而不是等級表的目標 E 比較（原本是「截斷後的 E」，同一個道理）：目標不可達時，
 *    調整完仍永遠差 > 0.10，提醒會一直出現。
 *
 * 其他持股永遠沒有 order — 這是結構性的：只有 0050 與台股正 2 類的成員會產生股數。價格 ≤ 0 或不是
 * 有限數字的標的無法換算股數，一律不列入 orders（呼叫端應先擋掉缺價格的情況）。A ≤ 0 → 不動。
 */
export function advise(
  prevState: BrakeState | null,
  state: BrakeState,
  profile: BrakeParams,
  holdings: ScopeHoldings,
  prices: StrategyPrices,
  band: number = REBALANCE_BAND,
): Advice {
  const eTarget = state === 'OFFENSE' ? profile.eOn : profile.eOff;
  const switched = (prevState ?? 'OFFENSE') !== state;

  const core = holdings['0050'] + holdings.lev2 + holdings.cash; // C
  const scopeAssets = core + holdings.othersValue; // A

  if (!(scopeAssets > 0)) {
    return {
      action: '不動',
      switched,
      scopeAssets: 0,
      eActual: 0,
      eTarget,
      eReach: 0,
      aboveTarget: false,
      capped: false,
      targets: { '0050': 0, lev2: 0, cash: 0 },
      weights: { '0050': 0, lev2: 0, cash: 0 },
      othersShare: 0,
      orders: [],
      lev2Change: 0,
      cashChange: 0,
    };
  }

  const eps = scopeAssets * FLOAT_TOLERANCE;
  const eActual = (holdings['0050'] + 2 * holdings.lev2 + holdings.othersExposure) / scopeAssets;

  const minCash = Math.min(core, profile.k * scopeAssets); // c_min
  const investable = core - minCash; // I
  const needed = eTarget * scopeAssets - holdings.othersExposure; // e*
  const aboveTarget = needed < -eps;
  const capped = needed > 2 * investable + eps;
  const reach = Math.min(Math.max(needed, 0), 2 * investable); // e

  const targets: CoreTargets =
    reach <= investable
      ? { '0050': reach, lev2: 0, cash: core - reach }
      : {
          '0050': Math.max(2 * investable - reach, 0),
          lev2: reach - investable,
          cash: minCash,
        };
  const eReach = (holdings.othersExposure + reach) / scopeAssets;

  const base = {
    switched,
    scopeAssets,
    eActual,
    eTarget,
    eReach,
    aboveTarget,
    capped,
    targets,
    weights: {
      '0050': targets['0050'] / scopeAssets,
      lev2: targets.lev2 / scopeAssets,
      cash: targets.cash / scopeAssets,
    },
    othersShare: holdings.othersValue / scopeAssets,
  };

  if (!switched && Math.abs(eActual - eReach) <= band) {
    return { ...base, action: '不動', orders: [], lev2Change: 0, cashChange: 0 };
  }

  const sells: Order[] = [];
  const buys: Order[] = [];
  let lev2Change = 0;
  let cashChange = 0;
  const push = (order: Order, price: number) => {
    (order.shares < 0 ? sells : buys).push(order);
    cashChange -= order.shares * price;
  };

  const price0050 = prices['0050'];
  if (isValidPrice(price0050)) {
    const shares = roundHalfAwayFromZero((targets['0050'] - holdings['0050']) / price0050);
    if (shares !== 0) push({ symbol: '0050', shares }, price0050);
  }
  const lev2Holdings = holdings.lev2Holdings ?? fallbackLev2Holdings(holdings, prices);
  for (const order of splitLev2Order(targets.lev2 - holdings.lev2, lev2Holdings, prices)) {
    const price = prices[order.symbol] as number; // splitLev2Order 只產生有有效價格的標的
    push(order, price);
    lev2Change += order.shares * price;
  }
  const orders = [...sells, ...buys]; // 先賣再買，避免現金不夠

  // PM 假設 A3：四捨五入後所有股數皆為 0 → 「不動」，而不是一個沒有動作的「加碼／減碼」。
  if (orders.length === 0) {
    return { ...base, action: '不動', orders: [], lev2Change: 0, cashChange: 0 };
  }

  return { ...base, action: eReach < eActual ? '減碼' : '加碼', orders, lev2Change, cashChange };
}

// ---- 使用者偏好（per-browser，同 rebalancingTarget.ts / feeDiscountRate.ts） ----

const STORAGE_KEY = 'portfolio.drawdownBrake';

export interface BrakeSettings {
  profile: ProfileKey;
  defenseE: DefenseE;
  /** 進階：覆寫等級預設的最低現金比例 k；未設定則用等級預設。 */
  minCashRatio?: number;
}

export const DEFAULT_BRAKE_SETTINGS: BrakeSettings = {
  profile: DEFAULT_PROFILE,
  defenseE: 0,
};

function isProfileKey(value: unknown): value is ProfileKey {
  return typeof value === 'string' && value in PROFILES;
}

/** localStorage 可能被手動改過或來自舊版：逐欄檢查，不合法的欄位退回預設。尤其 `defenseE` 只
 * 認 0 與 0.3 — 被竄改成 ≥ 0.5 也不會生效（DECISION-013）。舊版存的 `strategyCashTWD`（需求 12 已
 * 移除策略現金）直接忽略，不報錯。 */
export function sanitizeBrakeSettings(raw: unknown): BrakeSettings {
  if (typeof raw !== 'object' || raw === null) return { ...DEFAULT_BRAKE_SETTINGS };
  const r = raw as Record<string, unknown>;
  const settings: BrakeSettings = {
    profile: isProfileKey(r.profile) ? r.profile : DEFAULT_BRAKE_SETTINGS.profile,
    defenseE: r.defenseE === 0.3 ? 0.3 : 0,
  };
  if (
    typeof r.minCashRatio === 'number' &&
    Number.isFinite(r.minCashRatio) &&
    r.minCashRatio >= MIN_CASH_RATIO_MIN &&
    r.minCashRatio <= MIN_CASH_RATIO_MAX
  ) {
    settings.minCashRatio = r.minCashRatio;
  }
  return settings;
}

export function loadBrakeSettings(): BrakeSettings {
  return sanitizeBrakeSettings(readJson<unknown>(STORAGE_KEY));
}

export function saveBrakeSettings(settings: BrakeSettings): void {
  writeJson(STORAGE_KEY, settings);
}

/** 設定 → `advise` / `composition` 用的 (進攻 E, 防守 E, k)。 */
export function resolveParams(settings: BrakeSettings): BrakeParams {
  const profile = PROFILES[settings.profile];
  return {
    eOn: profile.eOn,
    eOff: settings.defenseE,
    k: settings.minCashRatio ?? profile.k,
  };
}
