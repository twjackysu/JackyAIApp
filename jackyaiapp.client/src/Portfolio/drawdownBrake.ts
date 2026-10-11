import { readJson, writeJson } from '@/utils/storage';

/**
 * 「回落煞車」(Issue #431 / DECISION-013) — 0050 + 00631L + 現金 的目標配置與加減碼建議。
 *
 * 這個檔案是 #431 §3、§4 與其 Python 參考實作的逐字移植。規則與固定參數屬 Boss 的回測結論，
 * 不得調整，也不得新增選項（防守 E ≥ 0.5、不煞車的固定槓桿、突破新高減碼、深跌提前加碼、階梯式
 * 加碼一律不做）。大盤訊號本身（狀態機）由後端計算；這裡只負責「個人建議」。
 */

export type BrakeState = 'OFFENSE' | 'DEFENSE';
export type StrategySymbol = '0050' | '00631L';
export type ProfileKey = 'conservative' | 'balanced' | 'aggressive';

/** E = 0050 權重 + 2 × 00631L 權重（有效大盤曝險）。 */
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

export interface Weights {
  '0050': number;
  '00631L': number;
  cash: number;
}

export interface Composition {
  weights: Weights;
  /** 實際用來配置的 E；目標 E 不可行時為截斷後的值。 */
  effectiveE: number;
  /** 目標 E 超過 2 × (1 − k)，已被截斷（UI 須提示「現金比例過高，無法達到此曝險」）。 */
  truncated: boolean;
}

/** 目標曝險 E 與最低現金比例 k → 0050 / 00631L / 現金 權重（#431 §3）。 */
export function composition(E: number, k: number): Composition {
  const inv = 1 - k; // 最多可投入的比例
  if (E <= inv) {
    return {
      weights: { '0050': E, '00631L': 0, cash: 1 - E },
      effectiveE: E,
      truncated: false,
    };
  }
  const truncatedE = Math.min(E, 2 * inv); // 不可行時截斷
  return {
    weights: { '0050': 2 * inv - truncatedE, '00631L': truncatedE - inv, cash: k },
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

/** 0050 / 00631L 的市值與策略現金（TWD）。 */
export interface StrategyHoldings {
  '0050': number;
  '00631L': number;
  cash: number;
}

/** 0050 / 00631L 的最新收盤價（TWD）。 */
export type StrategyPrices = Record<StrategySymbol, number>;

export type AdviceAction = '加碼' | '減碼' | '不動';

export interface Order {
  symbol: StrategySymbol;
  /** 有號股數：< 0 賣出、> 0 買進（不為 0）。 */
  shares: number;
}

export interface Advice {
  action: AdviceAction;
  /** 這次是否為狀態切換日（OFFENSE ↔ DEFENSE）。 */
  switched: boolean;
  /** V = 0050 市值 + 00631L 市值 + 策略現金。 */
  totalValue: number;
  eActual: number;
  /** 目標 E（若目標不可行，為截斷後的 E）。 */
  eTarget: number;
  /** 目標配置權重（即使「不動」也回傳，供 UI 顯示）。 */
  weights: Weights;
  truncated: boolean;
  /** 先賣後買；股數為 0 的標的不列入。「不動」時為空。 */
  orders: Order[];
}

const STRATEGY_SYMBOLS: StrategySymbol[] = ['0050', '00631L'];

/**
 * 狀態與持股 → 加碼／減碼／不動（#431 §4 / §7 `advise`）。
 *
 * 與參考實作的差異只有兩處，皆為保守且在預設參數下結果相同：
 * 1. `prevState` 為 null（序列第一天，沒有前一日）時視為初始狀態 OFFENSE，與狀態機的起始值一致。
 * 2. `E_target` 取「截斷後」的 E（§3：「以截斷後的 E 計算」）。參考實作拿未截斷的 E 比較偏離，
 *    在使用者把 k 調高到 E 不可行時，調整完仍永遠差 > 0.10，提醒會一直出現。
 *
 * 價格 ≤ 0 或不是有限數字的標的無法換算股數，一律不列入 orders（呼叫端應先擋掉缺價格的情況）。
 */
export function advise(
  prevState: BrakeState | null,
  state: BrakeState,
  profile: BrakeParams,
  holdings: StrategyHoldings,
  prices: StrategyPrices,
  band: number = REBALANCE_BAND,
): Advice {
  const target = composition(state === 'OFFENSE' ? profile.eOn : profile.eOff, profile.k);
  const eTarget = target.effectiveE;

  const totalValue = holdings['0050'] + holdings['00631L'] + holdings.cash;
  const eActual = totalValue > 0 ? (holdings['0050'] + 2 * holdings['00631L']) / totalValue : 0;
  const switched = (prevState ?? 'OFFENSE') !== state;

  const base = {
    switched,
    totalValue,
    eActual,
    eTarget,
    weights: target.weights,
    truncated: target.truncated,
  };

  if (!switched && Math.abs(eActual - eTarget) <= band) {
    return { ...base, action: '不動', orders: [] };
  }

  const sells: Order[] = [];
  const buys: Order[] = [];
  for (const symbol of STRATEGY_SYMBOLS) {
    const price = prices[symbol];
    if (!Number.isFinite(price) || price <= 0) continue;
    const shares = roundHalfAwayFromZero(
      (totalValue * target.weights[symbol] - holdings[symbol]) / price,
    );
    if (shares < 0) sells.push({ symbol, shares });
    else if (shares > 0) buys.push({ symbol, shares });
  }
  const orders = [...sells, ...buys]; // 先賣再買，避免現金不夠

  // PM 假設 A3：四捨五入後所有股數皆為 0 → 「不動」，而不是一個沒有動作的「加碼／減碼」。
  if (orders.length === 0) return { ...base, action: '不動', orders: [] };

  return { ...base, action: eTarget < eActual ? '減碼' : '加碼', orders };
}

// ---- 使用者偏好（per-browser，同 rebalancingTarget.ts / feeDiscountRate.ts） ----

const STORAGE_KEY = 'portfolio.drawdownBrake';

export interface BrakeSettings {
  profile: ProfileKey;
  defenseE: DefenseE;
  /** 進階：覆寫等級預設的最低現金比例 k；未設定則用等級預設。 */
  minCashRatio?: number;
  /** 使用者指定給這個策略用的現金（NT$）。 */
  strategyCashTWD: number;
}

export const DEFAULT_BRAKE_SETTINGS: BrakeSettings = {
  profile: DEFAULT_PROFILE,
  defenseE: 0,
  strategyCashTWD: 0,
};

function isProfileKey(value: unknown): value is ProfileKey {
  return typeof value === 'string' && value in PROFILES;
}

/** localStorage 可能被手動改過或來自舊版：逐欄檢查，不合法的欄位退回預設。尤其 `defenseE` 只
 * 認 0 與 0.3 — 被竄改成 ≥ 0.5 也不會生效（DECISION-013）。 */
export function sanitizeBrakeSettings(raw: unknown): BrakeSettings {
  if (typeof raw !== 'object' || raw === null) return { ...DEFAULT_BRAKE_SETTINGS };
  const r = raw as Record<string, unknown>;
  const settings: BrakeSettings = {
    profile: isProfileKey(r.profile) ? r.profile : DEFAULT_BRAKE_SETTINGS.profile,
    defenseE: r.defenseE === 0.3 ? 0.3 : 0,
    strategyCashTWD:
      typeof r.strategyCashTWD === 'number' &&
      Number.isFinite(r.strategyCashTWD) &&
      r.strategyCashTWD > 0
        ? r.strategyCashTWD
        : 0,
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
