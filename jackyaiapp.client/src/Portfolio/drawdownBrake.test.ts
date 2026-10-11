import { beforeEach, describe, expect, it } from 'vitest';

import {
  advise,
  BrakeParams,
  composition,
  DEFAULT_BRAKE_SETTINGS,
  DEFENSE_E_OPTIONS,
  loadBrakeSettings,
  PROFILES,
  resolveParams,
  roundHalfAwayFromZero,
  sanitizeBrakeSettings,
  saveBrakeSettings,
} from './drawdownBrake';

const BALANCED: BrakeParams = { eOn: 1.4, eOff: 0, k: 0.2 };

describe('PROFILES', () => {
  it('固定的三個風險等級 (進攻 E, 防守 E, k)，預設與 #431 §2 一致', () => {
    expect(PROFILES.conservative).toMatchObject({ eOn: 1.0, eOff: 0, k: 0.2 });
    expect(PROFILES.balanced).toMatchObject({ eOn: 1.4, eOff: 0, k: 0.2 });
    expect(PROFILES.aggressive).toMatchObject({ eOn: 1.8, eOff: 0, k: 0.1 });
    expect(DEFAULT_BRAKE_SETTINGS.profile).toBe('balanced');
  });

  it('防守 E 只有 0 與 0.3 兩個選項（沒有 ≥ 0.5）', () => {
    expect([...DEFENSE_E_OPTIONS]).toEqual([0, 0.3]);
  });
});

describe('composition (#431 §3 / §9)', () => {
  it('(1.4, 0.2) → 0050 20% / 00631L 60% / 現金 20%', () => {
    const c = composition(1.4, 0.2);
    expect(c.weights['0050']).toBeCloseTo(0.2, 12);
    expect(c.weights['00631L']).toBeCloseTo(0.6, 12);
    expect(c.weights.cash).toBeCloseTo(0.2, 12);
    expect(c.truncated).toBe(false);
  });

  it('(1.0, 0.2) → 60% / 20% / 20%', () => {
    const c = composition(1.0, 0.2);
    expect(c.weights['0050']).toBeCloseTo(0.6, 12);
    expect(c.weights['00631L']).toBeCloseTo(0.2, 12);
    expect(c.weights.cash).toBeCloseTo(0.2, 12);
    expect(c.truncated).toBe(false);
  });

  it('(1.8, 0.2) 不可行：截斷為 E=1.6 → 0 / 80% / 20%，且標記 truncated', () => {
    const c = composition(1.8, 0.2);
    expect(c.effectiveE).toBeCloseTo(1.6, 12);
    expect(c.weights['0050']).toBeCloseTo(0, 12);
    expect(c.weights['00631L']).toBeCloseTo(0.8, 12);
    expect(c.weights.cash).toBeCloseTo(0.2, 12);
    expect(c.truncated).toBe(true);
  });

  it('(0, 0.2) → 現金 100%', () => {
    const c = composition(0, 0.2);
    expect(c.weights).toEqual({ '0050': 0, '00631L': 0, cash: 1 });
    expect(c.truncated).toBe(false);
  });

  it('(1.8, 0.1)（積極）剛好可行 → 0 / 90% / 10%，不算截斷', () => {
    const c = composition(1.8, 0.1);
    expect(c.weights['0050']).toBeCloseTo(0, 12);
    expect(c.weights['00631L']).toBeCloseTo(0.9, 12);
    expect(c.weights.cash).toBeCloseTo(0.1, 12);
    expect(c.truncated).toBe(false);
  });

  it('(0.3, 0.2)（防守 E=0.3）→ 0050 30% / 現金 70%', () => {
    const c = composition(0.3, 0.2);
    expect(c.weights).toEqual({ '0050': 0.3, '00631L': 0, cash: 0.7 });
  });

  it('權重總和永遠是 1', () => {
    for (const [E, k] of [
      [1.4, 0.2],
      [1.0, 0.2],
      [1.8, 0.2],
      [1.8, 0.1],
      [0.3, 0.5],
      [1.4, 0.5],
    ]) {
      const { weights } = composition(E, k);
      expect(weights['0050'] + weights['00631L'] + weights.cash).toBeCloseTo(1, 12);
    }
  });
});

describe('roundHalfAwayFromZero', () => {
  it('.5 一律遠離 0（不是 Math.round 的往上，也不是銀行家捨入）', () => {
    expect(roundHalfAwayFromZero(2.5)).toBe(3);
    expect(roundHalfAwayFromZero(-2.5)).toBe(-3);
    expect(roundHalfAwayFromZero(1.5)).toBe(2);
    expect(roundHalfAwayFromZero(-1.5)).toBe(-2);
    expect(roundHalfAwayFromZero(0.5)).toBe(1);
    expect(roundHalfAwayFromZero(-0.5)).toBe(-1);
  });

  it('非 .5 照常四捨五入，且不會回傳 -0', () => {
    expect(roundHalfAwayFromZero(2.4)).toBe(2);
    expect(roundHalfAwayFromZero(-2.6)).toBe(-3);
    expect(Object.is(roundHalfAwayFromZero(-0.4), 0)).toBe(true);
    expect(Object.is(roundHalfAwayFromZero(0), 0)).toBe(true);
  });
});

describe('advise (#431 §4 / §7)', () => {
  const prices = { '0050': 60, '00631L': 100 };

  it('狀態未變、偏離 ≤ 0.10 → 「不動」且沒有 orders', () => {
    // V = 1,000,000；E_actual = (200k + 2 × 620k) / 1M = 1.44，離目標 1.4 只差 0.04
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      { '0050': 200_000, '00631L': 620_000, cash: 180_000 },
      prices,
    );
    expect(advice.action).toBe('不動');
    expect(advice.orders).toEqual([]);
    expect(advice.switched).toBe(false);
    expect(advice.eActual).toBeCloseTo(1.44, 12);
    expect(advice.eTarget).toBe(1.4);
  });

  it('偏離 > 0.10 → 再平衡：曝險過高 = 減碼，先賣後買', () => {
    // E_actual = 2 × 765k / 1M = 1.53 > 1.4 + 0.10
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      { '0050': 0, '00631L': 765_000, cash: 235_000 },
      prices,
    );
    expect(advice.switched).toBe(false);
    expect(advice.action).toBe('減碼');
    // 0050: 200,000 / 60 = 3333.33 → +3333；00631L: (600,000 − 765,000) / 100 = −1650
    expect(advice.orders).toEqual([
      { symbol: '00631L', shares: -1650 },
      { symbol: '0050', shares: 3333 },
    ]);
  });

  it('偏離 > 0.10 → 再平衡：曝險過低 = 加碼', () => {
    // E_actual = 2 × 400k / 1M = 0.8，低於 1.4 − 0.10
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      { '0050': 0, '00631L': 400_000, cash: 600_000 },
      prices,
    );
    expect(advice.action).toBe('加碼');
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: 3333 },
      { symbol: '00631L', shares: 2000 },
    ]);
  });

  it('OFFENSE → DEFENSE 一定調整：全部賣出轉現金，減碼', () => {
    const advice = advise(
      'OFFENSE',
      'DEFENSE',
      BALANCED,
      { '0050': 200_000, '00631L': 600_000, cash: 200_000 },
      { '0050': 100, '00631L': 50 },
    );
    expect(advice.switched).toBe(true);
    expect(advice.action).toBe('減碼');
    expect(advice.eTarget).toBe(0);
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: -2000 },
      { symbol: '00631L', shares: -12000 },
    ]);
  });

  it('DEFENSE → OFFENSE 一定調整：全現金買進到目標配置，加碼', () => {
    const advice = advise(
      'DEFENSE',
      'OFFENSE',
      BALANCED,
      { '0050': 0, '00631L': 0, cash: 1_000_000 },
      { '0050': 100, '00631L': 50 },
    );
    expect(advice.switched).toBe(true);
    expect(advice.action).toBe('加碼');
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: 2000 },
      { symbol: '00631L', shares: 12000 },
    ]);
  });

  it('狀態切換但持股早已在目標配置（所有股數皆為 0）→ 「不動」而不是空的加碼（PM 假設 A3）', () => {
    const advice = advise(
      'DEFENSE',
      'OFFENSE',
      BALANCED,
      { '0050': 200_000, '00631L': 600_000, cash: 200_000 },
      { '0050': 100, '00631L': 50 },
    );
    expect(advice.switched).toBe(true);
    expect(advice.action).toBe('不動');
    expect(advice.orders).toEqual([]);
  });

  it('負數與正數的 .5 都遠離 0 捨入', () => {
    // k=0.25、E=1.0 → 0050 50% / 00631L 25% / 現金 25%（二進位精確，不會有浮點誤差）
    const params: BrakeParams = { eOn: 1.0, eOff: 0, k: 0.25 };
    const sell = advise(
      'DEFENSE',
      'OFFENSE',
      params,
      { '0050': 500_250, '00631L': 250_000, cash: 249_750 },
      { '0050': 100, '00631L': 100 },
    );
    expect(sell.orders).toEqual([{ symbol: '0050', shares: -3 }]); // −2.5 → −3（Math.round 會給 −2）
    const buy = advise(
      'DEFENSE',
      'OFFENSE',
      params,
      { '0050': 499_750, '00631L': 250_000, cash: 250_250 },
      { '0050': 100, '00631L': 100 },
    );
    expect(buy.orders).toEqual([{ symbol: '0050', shares: 3 }]); // 2.5 → 3（銀行家捨入會給 2）
  });

  it('賣出一定排在買進之前', () => {
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      { '0050': 0, '00631L': 765_000, cash: 235_000 },
      prices,
    );
    const firstBuy = advice.orders.findIndex((o) => o.shares > 0);
    const lastSell = advice.orders.map((o) => o.shares < 0).lastIndexOf(true);
    expect(lastSell).toBeLessThan(firstBuy);
  });

  it('V = 0 不會除以零：不動、沒有 orders，且數值都是有限數', () => {
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      { '0050': 0, '00631L': 0, cash: 0 },
      prices,
    );
    expect(advice.action).toBe('不動');
    expect(advice.orders).toEqual([]);
    expect(advice.totalValue).toBe(0);
    expect(Number.isFinite(advice.eActual)).toBe(true);
  });

  it('prevState 為 null 視為初始狀態 OFFENSE：OFFENSE 不算切換、DEFENSE 算切換', () => {
    const holdings = { '0050': 200_000, '00631L': 600_000, cash: 200_000 };
    expect(advise(null, 'OFFENSE', BALANCED, holdings, prices).switched).toBe(false);
    expect(advise(null, 'DEFENSE', BALANCED, holdings, prices).switched).toBe(true);
  });

  it('目標 E 不可行（k 太高）時用截斷後的 E 比較，達成後不會永遠被判偏離', () => {
    // E=1.8、k=0.2 → 截斷為 1.6（0 / 80% / 20%）。持股已在截斷後的目標。
    const params: BrakeParams = { eOn: 1.8, eOff: 0, k: 0.2 };
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      params,
      { '0050': 0, '00631L': 800_000, cash: 200_000 },
      prices,
    );
    expect(advice.truncated).toBe(true);
    expect(advice.eTarget).toBeCloseTo(1.6, 12);
    expect(advice.action).toBe('不動');
  });

  it('價格缺失（≤ 0）的標的不產生 order，也不產生 NaN／Infinity', () => {
    const advice = advise(
      'DEFENSE',
      'OFFENSE',
      BALANCED,
      { '0050': 0, '00631L': 0, cash: 1_000_000 },
      { '0050': 0, '00631L': 50 },
    );
    expect(advice.orders).toEqual([{ symbol: '00631L', shares: 12000 }]);
  });

  it('防守 E=0.3：進入防守時保留 0050 30%', () => {
    const params: BrakeParams = { eOn: 1.4, eOff: 0.3, k: 0.2 };
    const advice = advise(
      'OFFENSE',
      'DEFENSE',
      params,
      { '0050': 200_000, '00631L': 600_000, cash: 200_000 },
      { '0050': 100, '00631L': 50 },
    );
    expect(advice.weights['0050']).toBe(0.3);
    expect(advice.orders).toEqual([
      { symbol: '00631L', shares: -12000 },
      { symbol: '0050', shares: 1000 },
    ]);
  });
});

describe('brake settings (localStorage)', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('沒存過 → 預設（平衡、防守 E=0、策略現金 0）', () => {
    expect(loadBrakeSettings()).toEqual(DEFAULT_BRAKE_SETTINGS);
  });

  it('存了再讀回來', () => {
    saveBrakeSettings({
      profile: 'aggressive',
      defenseE: 0.3,
      minCashRatio: 0.15,
      strategyCashTWD: 500_000,
    });
    expect(loadBrakeSettings()).toEqual({
      profile: 'aggressive',
      defenseE: 0.3,
      minCashRatio: 0.15,
      strategyCashTWD: 500_000,
    });
  });

  it('寫在 portfolio.drawdownBrake 這個 key', () => {
    saveBrakeSettings({ ...DEFAULT_BRAKE_SETTINGS, strategyCashTWD: 1 });
    expect(localStorage.getItem('portfolio.drawdownBrake')).not.toBeNull();
  });

  it('被改壞的內容退回預設：壞掉的 JSON、非物件', () => {
    localStorage.setItem('portfolio.drawdownBrake', '{not json');
    expect(loadBrakeSettings()).toEqual(DEFAULT_BRAKE_SETTINGS);
    localStorage.setItem('portfolio.drawdownBrake', '"balanced"');
    expect(loadBrakeSettings()).toEqual(DEFAULT_BRAKE_SETTINGS);
  });

  it('防守 E 只認 0 與 0.3：被竄改成 0.5 / 1 / 字串都不生效', () => {
    for (const bad of [0.5, 0.8, 1, '0.3', -1, null]) {
      expect(sanitizeBrakeSettings({ defenseE: bad }).defenseE).toBe(0);
    }
    expect(sanitizeBrakeSettings({ defenseE: 0.3 }).defenseE).toBe(0.3);
  });

  it('未知的風險等級、超出 0~0.5 的 k、負的或非數字的策略現金逐欄退回預設', () => {
    const s = sanitizeBrakeSettings({
      profile: 'yolo',
      minCashRatio: 0.9,
      strategyCashTWD: -5,
    });
    expect(s).toEqual(DEFAULT_BRAKE_SETTINGS);
    expect(sanitizeBrakeSettings({ minCashRatio: 'x', strategyCashTWD: 'x' })).toEqual(
      DEFAULT_BRAKE_SETTINGS,
    );
    expect(sanitizeBrakeSettings({ minCashRatio: 0 }).minCashRatio).toBe(0);
    expect(sanitizeBrakeSettings({ minCashRatio: 0.5 }).minCashRatio).toBe(0.5);
  });

  it('resolveParams：等級預設、防守 E 與 k 的覆寫', () => {
    expect(resolveParams(DEFAULT_BRAKE_SETTINGS)).toEqual({ eOn: 1.4, eOff: 0, k: 0.2 });
    expect(
      resolveParams({ ...DEFAULT_BRAKE_SETTINGS, profile: 'aggressive', defenseE: 0.3 }),
    ).toEqual({ eOn: 1.8, eOff: 0.3, k: 0.1 });
    expect(resolveParams({ ...DEFAULT_BRAKE_SETTINGS, minCashRatio: 0 }).k).toBe(0);
  });
});
