import { beforeEach, describe, expect, it } from 'vitest';

import {
  advise,
  BrakeParams,
  BrakeState,
  composition,
  DEFAULT_BRAKE_SETTINGS,
  DEFENSE_E_OPTIONS,
  loadBrakeSettings,
  PROFILE_KEYS,
  PROFILES,
  resolveParams,
  roundHalfAwayFromZero,
  sanitizeBrakeSettings,
  saveBrakeSettings,
  ScopeHoldings,
  splitLev2Order,
  StrategyPrices,
} from './drawdownBrake';

const BALANCED: BrakeParams = { eOn: 1.4, eOff: 0, k: 0.2 };

/** 沒有其他持股的範圍內持股（退化情況：與原本只看 0050 + 00631L + 現金的算法相同）。 */
function scope(v50: number, v631: number, cash: number): ScopeHoldings {
  return { '0050': v50, lev2: v631, cash, othersValue: 0, othersExposure: 0 };
}

/** 有其他持股：`value` 為市值、`factor` 為它們的平均曝險倍數（1 ＝ 一倍商品）。 */
function withOthers(
  v50: number,
  v631: number,
  cash: number,
  value: number,
  factor = 1,
): ScopeHoldings {
  return { '0050': v50, lev2: v631, cash, othersValue: value, othersExposure: value * factor };
}

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
    expect(c.weights.lev2).toBeCloseTo(0.6, 12);
    expect(c.weights.cash).toBeCloseTo(0.2, 12);
    expect(c.truncated).toBe(false);
  });

  it('(1.0, 0.2) → 60% / 20% / 20%', () => {
    const c = composition(1.0, 0.2);
    expect(c.weights['0050']).toBeCloseTo(0.6, 12);
    expect(c.weights.lev2).toBeCloseTo(0.2, 12);
    expect(c.weights.cash).toBeCloseTo(0.2, 12);
    expect(c.truncated).toBe(false);
  });

  it('(1.8, 0.2) 不可行：截斷為 E=1.6 → 0 / 80% / 20%，且標記 truncated', () => {
    const c = composition(1.8, 0.2);
    expect(c.effectiveE).toBeCloseTo(1.6, 12);
    expect(c.weights['0050']).toBeCloseTo(0, 12);
    expect(c.weights.lev2).toBeCloseTo(0.8, 12);
    expect(c.weights.cash).toBeCloseTo(0.2, 12);
    expect(c.truncated).toBe(true);
  });

  it('(0, 0.2) → 現金 100%', () => {
    const c = composition(0, 0.2);
    expect(c.weights).toEqual({ '0050': 0, lev2: 0, cash: 1 });
    expect(c.truncated).toBe(false);
  });

  it('(1.8, 0.1)（積極）剛好可行 → 0 / 90% / 10%，不算截斷', () => {
    const c = composition(1.8, 0.1);
    expect(c.weights['0050']).toBeCloseTo(0, 12);
    expect(c.weights.lev2).toBeCloseTo(0.9, 12);
    expect(c.weights.cash).toBeCloseTo(0.1, 12);
    expect(c.truncated).toBe(false);
  });

  it('(0.3, 0.2)（防守 E=0.3）→ 0050 30% / 現金 70%', () => {
    const c = composition(0.3, 0.2);
    expect(c.weights).toEqual({ '0050': 0.3, lev2: 0, cash: 0.7 });
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
      expect(weights['0050'] + weights.lev2 + weights.cash).toBeCloseTo(1, 12);
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
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, scope(200_000, 620_000, 180_000), prices);
    expect(advice.action).toBe('不動');
    expect(advice.orders).toEqual([]);
    expect(advice.switched).toBe(false);
    expect(advice.eActual).toBeCloseTo(1.44, 12);
    expect(advice.eTarget).toBe(1.4);
  });

  it('偏離 > 0.10 → 再平衡：曝險過高 = 減碼，先賣後買', () => {
    // E_actual = 2 × 765k / 1M = 1.53 > 1.4 + 0.10
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, scope(0, 765_000, 235_000), prices);
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
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, scope(0, 400_000, 600_000), prices);
    expect(advice.action).toBe('加碼');
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: 3333 },
      { symbol: '00631L', shares: 2000 },
    ]);
  });

  it('OFFENSE → DEFENSE 一定調整：全部賣出轉現金，減碼', () => {
    const advice = advise('OFFENSE', 'DEFENSE', BALANCED, scope(200_000, 600_000, 200_000), {
      '0050': 100,
      '00631L': 50,
    });
    expect(advice.switched).toBe(true);
    expect(advice.action).toBe('減碼');
    expect(advice.eTarget).toBe(0);
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: -2000 },
      { symbol: '00631L', shares: -12000 },
    ]);
  });

  it('DEFENSE → OFFENSE 一定調整：全現金買進到目標配置，加碼', () => {
    const advice = advise('DEFENSE', 'OFFENSE', BALANCED, scope(0, 0, 1_000_000), {
      '0050': 100,
      '00631L': 50,
    });
    expect(advice.switched).toBe(true);
    expect(advice.action).toBe('加碼');
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: 2000 },
      { symbol: '00631L', shares: 12000 },
    ]);
  });

  it('狀態切換但持股早已在目標配置（所有股數皆為 0）→ 「不動」而不是空的加碼（PM 假設 A3）', () => {
    const advice = advise('DEFENSE', 'OFFENSE', BALANCED, scope(200_000, 600_000, 200_000), {
      '0050': 100,
      '00631L': 50,
    });
    expect(advice.switched).toBe(true);
    expect(advice.action).toBe('不動');
    expect(advice.orders).toEqual([]);
  });

  it('負數與正數的 .5 都遠離 0 捨入', () => {
    // k=0.25、E=1.0 → 0050 50% / 00631L 25% / 現金 25%（二進位精確，不會有浮點誤差）
    const params: BrakeParams = { eOn: 1.0, eOff: 0, k: 0.25 };
    const sell = advise('DEFENSE', 'OFFENSE', params, scope(500_250, 250_000, 249_750), {
      '0050': 100,
      '00631L': 100,
    });
    expect(sell.orders).toEqual([{ symbol: '0050', shares: -3 }]); // −2.5 → −3（Math.round 會給 −2）
    const buy = advise('DEFENSE', 'OFFENSE', params, scope(499_750, 250_000, 250_250), {
      '0050': 100,
      '00631L': 100,
    });
    expect(buy.orders).toEqual([{ symbol: '0050', shares: 3 }]); // 2.5 → 3（銀行家捨入會給 2）
  });

  it('賣出一定排在買進之前', () => {
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, scope(0, 765_000, 235_000), prices);
    const firstBuy = advice.orders.findIndex((o) => o.shares > 0);
    const lastSell = advice.orders.map((o) => o.shares < 0).lastIndexOf(true);
    expect(lastSell).toBeLessThan(firstBuy);
  });

  it('V = 0 不會除以零：不動、沒有 orders，且數值都是有限數', () => {
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, scope(0, 0, 0), prices);
    expect(advice.action).toBe('不動');
    expect(advice.orders).toEqual([]);
    expect(advice.scopeAssets).toBe(0);
    expect(Number.isFinite(advice.eActual)).toBe(true);
    expect(advice.eReach).toBe(0);
    expect(advice.cashChange).toBe(0);
  });

  it('prevState 為 null 視為初始狀態 OFFENSE：OFFENSE 不算切換、DEFENSE 算切換', () => {
    const holdings = scope(200_000, 600_000, 200_000);
    expect(advise(null, 'OFFENSE', BALANCED, holdings, prices).switched).toBe(false);
    expect(advise(null, 'DEFENSE', BALANCED, holdings, prices).switched).toBe(true);
  });

  it('目標 E 不可行（k 太高）時用可達的 E 比較，達成後不會永遠被判偏離', () => {
    // E=1.8、k=0.2 → 截斷為 1.6（0 / 80% / 20%）。持股已在截斷後的目標。
    const params: BrakeParams = { eOn: 1.8, eOff: 0, k: 0.2 };
    const advice = advise('OFFENSE', 'OFFENSE', params, scope(0, 800_000, 200_000), prices);
    expect(advice.capped).toBe(true);
    expect(advice.eTarget).toBe(1.8); // 等級表的目標，不截斷
    expect(advice.eReach).toBeCloseTo(1.6, 12); // 可達曝險＝原本截斷後的 E
    expect(advice.action).toBe('不動');
  });

  it('價格缺失（≤ 0）的標的不產生 order，也不產生 NaN／Infinity', () => {
    const advice = advise('DEFENSE', 'OFFENSE', BALANCED, scope(0, 0, 1_000_000), {
      '0050': 0,
      '00631L': 50,
    });
    expect(advice.orders).toEqual([{ symbol: '00631L', shares: 12000 }]);
  });

  it('防守 E=0.3：進入防守時保留 0050 30%', () => {
    const params: BrakeParams = { eOn: 1.4, eOff: 0.3, k: 0.2 };
    const advice = advise('OFFENSE', 'DEFENSE', params, scope(200_000, 600_000, 200_000), {
      '0050': 100,
      '00631L': 50,
    });
    expect(advice.weights['0050']).toBeCloseTo(0.3, 12);
    expect(advice.orders).toEqual([
      { symbol: '00631L', shares: -12000 },
      { symbol: '0050', shares: 1000 },
    ]);
  });
});

describe('advise — 整個流動資產（需求 12c）', () => {
  const M = 1_000_000;
  // 價格：0050 ＝ 60、00631L ＝ 30（示意值，不是實際持股；ARCHITECTURE 情境表同一組）。
  const prices = { '0050': 60, '00631L': 30 };

  describe('退化對照：其他持股為 0 時與 composition 完全相同', () => {
    const holdingsCases: ScopeHoldings[] = [
      scope(200_000, 600_000, 200_000),
      scope(0, 765_000, 235_000),
      scope(0, 0, 1_000_000),
      scope(500_000, 0, 500_000),
      scope(123_456, 789_012, 345_678),
    ];
    const states: BrakeState[] = ['OFFENSE', 'DEFENSE'];

    for (const key of PROFILE_KEYS) {
      for (const state of states) {
        it(`${PROFILES[key].label}／${state === 'OFFENSE' ? '進攻' : '防守'}：t／A ＝ composition(E, k)`, () => {
          const profile = PROFILES[key];
          const E = state === 'OFFENSE' ? profile.eOn : profile.eOff;
          const expected = composition(E, profile.k);
          for (const holdings of holdingsCases) {
            const advice = advise(
              state === 'OFFENSE' ? 'DEFENSE' : 'OFFENSE',
              state,
              profile,
              holdings,
              prices,
            );
            const A = holdings['0050'] + holdings.lev2 + holdings.cash;
            expect(advice.scopeAssets).toBe(A);
            expect(advice.targets['0050'] / A).toBeCloseTo(expected.weights['0050'], 9);
            expect(advice.targets.lev2 / A).toBeCloseTo(expected.weights.lev2, 9);
            expect(advice.targets.cash / A).toBeCloseTo(expected.weights.cash, 9);
            expect(advice.eReach).toBeCloseTo(expected.effectiveE, 9);
            expect(advice.capped).toBe(expected.truncated);
            expect(advice.aboveTarget).toBe(false);
            expect(advice.eTarget).toBe(E);
          }
        });
      }
    }
  });

  describe('golden（ARCHITECTURE 三情境；平衡 E_on ＝ 1.4、k ＝ 0.2）', () => {
    it('S1 進攻、可達：賣 0050 58,333 股、買 00631L 116,667 股；現金類不動；執行後再算為「不動」', () => {
      // 0050 4.0／00631L 2.5／現金 2.0／其他一倍 1.5（NTSD 1.0 已被排除，不在這裡）
      const holdings = withOthers(4 * M, 2.5 * M, 2 * M, 1.5 * M);
      const advice = advise('DEFENSE', 'OFFENSE', BALANCED, holdings, prices);
      expect(advice.scopeAssets).toBe(10 * M);
      expect(advice.eActual).toBeCloseTo(1.05, 12);
      expect(advice.eReach).toBeCloseTo(1.4, 12);
      expect(advice.action).toBe('加碼');
      expect(advice.orders).toEqual([
        { symbol: '0050', shares: -58_333 },
        { symbol: '00631L', shares: 116_667 },
      ]);
      expect(advice.targets['0050']).toBeCloseTo(0.5 * M, 6);
      expect(advice.targets.lev2).toBeCloseTo(6 * M, 6);
      expect(advice.targets.cash).toBeCloseTo(2 * M, 6);
      expect(advice.aboveTarget).toBe(false);
      expect(advice.capped).toBe(false);
      // 現金類金額變動 ＝ −Σ(實際建議股數 × 價格)：賣 0050 進 3,499,980，買 00631L 出 3,500,010
      expect(advice.cashChange).toBeCloseTo(58_333 * 60 - 116_667 * 30, 6);

      // 執行後（忽略取整）再算：不動
      const after = withOthers(0.5 * M, 6 * M, 2 * M, 1.5 * M);
      const again = advise('DEFENSE', 'OFFENSE', BALANCED, after, prices);
      expect(again.action).toBe('不動');
      expect(again.orders).toEqual([]);
    });

    it('S2 切換到防守、其他持股單獨超過目標：賣 0050 41,667 股、賣 00631L 83,333 股，aboveTarget', () => {
      // 0050 2.5／00631L 2.5／現金 1.0／其他一倍 4.0
      const holdings = withOthers(2.5 * M, 2.5 * M, 1 * M, 4 * M);
      const advice = advise('OFFENSE', 'DEFENSE', BALANCED, holdings, prices);
      expect(advice.eActual).toBeCloseTo(1.15, 12);
      expect(advice.eTarget).toBe(0);
      expect(advice.eReach).toBeCloseTo(0.4, 12);
      expect(advice.aboveTarget).toBe(true);
      expect(advice.capped).toBe(false);
      expect(advice.action).toBe('減碼');
      expect(advice.orders).toEqual([
        { symbol: '0050', shares: -41_667 },
        { symbol: '00631L', shares: -83_333 },
      ]);
      expect(advice.targets).toEqual({ '0050': 0, lev2: 0, cash: 6 * M });

      // 執行後：核心降到最低；仍是切換日（switched ＝ true），但沒有可執行的動作 → 「不動」＋同一標示
      const after = withOthers(0, 0, 6 * M, 4 * M);
      const again = advise('OFFENSE', 'DEFENSE', BALANCED, after, prices);
      expect(again.action).toBe('不動');
      expect(again.orders).toEqual([]);
      expect(again.aboveTarget).toBe(true);
      expect(again.eReach).toBeCloseTo(0.4, 12);
    });

    it('S3 進攻、其他持股太多：不動＋capped（不得出現無法執行的建議）', () => {
      // 0050 0／00631L 1.0／現金 2.0／其他一倍 7.0
      const holdings = withOthers(0, 1 * M, 2 * M, 7 * M);
      const advice = advise('OFFENSE', 'OFFENSE', BALANCED, holdings, prices);
      expect(advice.eActual).toBeCloseTo(0.9, 12);
      expect(advice.eReach).toBeCloseTo(0.9, 12);
      expect(advice.capped).toBe(true);
      expect(advice.aboveTarget).toBe(false);
      expect(advice.action).toBe('不動');
      expect(advice.orders).toEqual([]);
    });

    it('S3b 同一人 00631L 尚未買、現金 3.0（E_act 0.70）：加碼，買 00631L 33,333 股', () => {
      const holdings = withOthers(0, 0, 3 * M, 7 * M);
      const advice = advise('OFFENSE', 'OFFENSE', BALANCED, holdings, prices);
      expect(advice.eActual).toBeCloseTo(0.7, 12);
      expect(advice.eReach).toBeCloseTo(0.9, 12);
      expect(advice.capped).toBe(true);
      expect(advice.action).toBe('加碼');
      expect(advice.orders).toEqual([{ symbol: '00631L', shares: 33_333 }]);
    });
  });

  describe('其他持股永遠不動', () => {
    const others = [
      ['其他一倍商品', withOthers(1 * M, 3 * M, 1 * M, 5 * M, 1)],
      ['其他倍數商品（3 倍）', withOthers(1 * M, 3 * M, 1 * M, 2 * M, 3)],
      ['反向商品（−1 倍）', withOthers(1 * M, 3 * M, 1 * M, 2 * M, -1)],
    ] as const;

    for (const [name, holdings] of others) {
      for (const [prev, state] of [
        ['OFFENSE', 'OFFENSE'],
        ['OFFENSE', 'DEFENSE'],
        ['DEFENSE', 'OFFENSE'],
      ] as const) {
        it(`${name}：${prev} → ${state} 只可能動 0050／00631L，沒有其他標的的 order`, () => {
          const advice = advise(prev, state, BALANCED, holdings, prices);
          for (const order of advice.orders) {
            expect(['0050', '00631L']).toContain(order.symbol);
          }
          expect(Object.keys(advice.targets).sort()).toEqual(['0050', 'cash', 'lev2']);
        });
      }
    }
  });

  it('改變其他一倍商品的金額，會改變 E 與核心建議', () => {
    const small = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      withOthers(2 * M, 4 * M, 2 * M, 1 * M),
      prices,
    );
    const large = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      withOthers(2 * M, 4 * M, 2 * M, 3 * M),
      prices,
    );
    expect(large.eActual).not.toBeCloseTo(small.eActual, 6);
    expect(large.targets.lev2).not.toBeCloseTo(small.targets.lev2, 6);
  });

  it('A ＝ 0050 + 00631L + 現金類 + 其他持股；E ＝ (v50 + 2·v631 + X_o) ÷ A', () => {
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      withOthers(1 * M, 2 * M, 3 * M, 4 * M, 2),
      prices,
    );
    expect(advice.scopeAssets).toBe(10 * M);
    expect(advice.eActual).toBeCloseTo((1 * M + 4 * M + 8 * M) / (10 * M), 12);
  });

  it('核心目標各桶加上其他持股，合計 ＝ A；權重加上 othersShare ＝ 1', () => {
    const advice = advise(
      'DEFENSE',
      'OFFENSE',
      BALANCED,
      withOthers(1 * M, 2 * M, 3 * M, 4 * M),
      prices,
    );
    const { targets, weights } = advice;
    expect(targets['0050'] + targets.lev2 + targets.cash + 4 * M).toBeCloseTo(10 * M, 6);
    expect(weights['0050'] + weights.lev2 + weights.cash + advice.othersShare).toBeCloseTo(1, 12);
  });

  it('觸發比較「可達 E」：目標不可達時，調整完不會永遠被判偏離', () => {
    // S3 結構：E_act 0.9 ＝ E_reach 0.9，離目標 1.4 差 0.5，但不動
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      withOthers(0, 1 * M, 2 * M, 7 * M),
      prices,
    );
    expect(Math.abs(advice.eActual - advice.eTarget)).toBeGreaterThan(0.1);
    expect(advice.action).toBe('不動');
  });

  it('核心已降到最低（C ＝ 0 以外）、偏離 > 0.10 仍只給可執行的建議', () => {
    // 沒有核心資產，只有其他持股：沒有任何可調整的東西
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, withOthers(0, 0, 0, 5 * M), prices);
    expect(advice.action).toBe('不動');
    expect(advice.orders).toEqual([]);
    expect(advice.capped).toBe(true);
    expect(advice.eReach).toBeCloseTo(1, 12);
  });

  it('反向商品讓 X_o 為負：核心要補足更多曝險', () => {
    // 其他持股 2.0M、倍數 −1 → X_o ＝ −2.0M；A ＝ 10M；C ＝ 8M，c_min ＝ 2M，I ＝ 6M
    // e* ＝ 1.4 × 10M + 2M ＝ 16M > 2I ＝ 12M → capped，E_reach ＝ (−2M + 12M) ÷ 10M ＝ 1.0
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      withOthers(2 * M, 2 * M, 4 * M, 2 * M, -1),
      prices,
    );
    expect(advice.eActual).toBeCloseTo((2 * M + 4 * M - 2 * M) / (10 * M), 12);
    expect(advice.capped).toBe(true);
    expect(advice.eReach).toBeCloseTo(1.0, 12);
  });

  it('浮點容差：e* 只差微小的 TWD 不算 aboveTarget／capped', () => {
    // 防守 E ＝ 0 且其他持股曝險 ＝ 0 → e* ＝ 0，不是 aboveTarget
    const advice = advise(
      'OFFENSE',
      'DEFENSE',
      BALANCED,
      withOthers(1 * M, 1 * M, 1 * M, 1 * M, 0),
      prices,
    );
    expect(advice.aboveTarget).toBe(false);
    expect(advice.capped).toBe(false);
    // 恰好可達（e* ＝ 2I）也不算 capped
    const exact = advise(
      'OFFENSE',
      'OFFENSE',
      { eOn: 1.6, eOff: 0, k: 0.2 },
      scope(0, 800_000, 200_000),
      prices,
    );
    expect(exact.capped).toBe(false);
    expect(exact.eReach).toBeCloseTo(1.6, 12);
  });

  it('價格缺失的核心標的不產生 order，也不影響現金變動的計算', () => {
    const advice = advise('DEFENSE', 'OFFENSE', BALANCED, withOthers(0, 0, 1 * M, 1 * M), {
      '0050': 0,
      '00631L': 50,
    });
    expect(advice.orders.every((o) => o.symbol === '00631L')).toBe(true);
    expect(Number.isFinite(advice.cashChange)).toBe(true);
  });
});

describe('splitLev2Order — 台股正 2 類的金額變動分配到各檔（DECISION-021）', () => {
  const prices: StrategyPrices = { '0050': 60, '00631L': 30, '00685L': 20 };
  // 00631L 600（20 股 × 30）、00685L 400（20 股 × 20）：現值比例 60／40
  const both = [
    { symbol: '00631L' as const, value: 600, shares: 20 },
    { symbol: '00685L' as const, value: 400, shares: 20 },
  ];

  it('減碼：依各檔現值比例分攤賣出金額', () => {
    expect(splitLev2Order(-500, both, prices)).toEqual([
      { symbol: '00631L', shares: -10 }, // 300 ÷ 30
      { symbol: '00685L', shares: -10 }, // 200 ÷ 20
    ]);
  });

  it('減碼上限：每檔不會賣超過持有股數', () => {
    // 分攤後 00631L 該賣 24 股，但只有 20 股
    expect(splitLev2Order(-1_200, both, prices)).toEqual([
      { symbol: '00631L', shares: -20 },
      { symbol: '00685L', shares: -20 },
    ]);
  });

  it('取整：.5 一律遠離 0（賣 2.5 股 → 3 股，買 2.5 股 → 3 股）', () => {
    const only = [{ symbol: '00631L' as const, value: 3_000, shares: 100 }];
    expect(splitLev2Order(-75, only, prices)).toEqual([{ symbol: '00631L', shares: -3 }]);
    expect(splitLev2Order(75, only, prices)).toEqual([{ symbol: '00631L', shares: 3 }]);
  });

  it('只持有一檔：全部給那一檔（減碼、加碼都是）', () => {
    const only00685L = [{ symbol: '00685L' as const, value: 5_000, shares: 250 }];
    expect(splitLev2Order(-1_100, only00685L, prices)).toEqual([{ symbol: '00685L', shares: -55 }]);
    expect(splitLev2Order(1_100, only00685L, prices)).toEqual([{ symbol: '00685L', shares: 55 }]);
  });

  it('都沒持有：加碼全部買 00631L（回測標的）；00685L 的價格有沒有都不影響', () => {
    expect(splitLev2Order(900, [], prices)).toEqual([{ symbol: '00631L', shares: 30 }]);
    expect(splitLev2Order(900, [], { '00631L': 30 })).toEqual([{ symbol: '00631L', shares: 30 }]);
  });

  it('加碼：依各檔持有現值比例，只給有持有的', () => {
    expect(splitLev2Order(1_500, both, prices)).toEqual([
      { symbol: '00631L', shares: 30 }, // 900 ÷ 30
      { symbol: '00685L', shares: 30 }, // 600 ÷ 20
    ]);
    const only00631L = [{ symbol: '00631L' as const, value: 600, shares: 20 }];
    expect(splitLev2Order(1_500, only00631L, prices)).toEqual([{ symbol: '00631L', shares: 50 }]);
  });

  it('沒有變動（0、非有限數）→ 沒有 order', () => {
    expect(splitLev2Order(0, both, prices)).toEqual([]);
    expect(splitLev2Order(Number.NaN, both, prices)).toEqual([]);
    expect(splitLev2Order(Number.POSITIVE_INFINITY, both, prices)).toEqual([]);
  });

  it('缺價格（沒給、≤ 0）的標的不產生 order，也不影響另一檔', () => {
    expect(splitLev2Order(-500, both, { '00631L': 30 })).toEqual([
      { symbol: '00631L', shares: -10 },
    ]);
    expect(splitLev2Order(1_500, both, { ...prices, '00685L': 0 })).toEqual([
      { symbol: '00631L', shares: 30 },
    ]);
    expect(splitLev2Order(900, [], {})).toEqual([]);
  });

  it('現值為 0 的成員不參與分配；結果沒有 0 股，也沒有 -0', () => {
    const withZero = [...both, { symbol: '00631L' as const, value: 0, shares: 0 }];
    expect(splitLev2Order(-500, withZero, prices)).toHaveLength(2);
    // 金額小到取整後為 0 股
    expect(splitLev2Order(-10, both, prices)).toEqual([]);
    expect(splitLev2Order(10, both, prices)).toEqual([]);
  });
});

describe('advise — 台股正 2 類（DECISION-021；平衡等級，價格 0050＝60、00631L＝30、00685L＝20）', () => {
  const M = 1_000_000;
  const prices: StrategyPrices = { '0050': 60, '00631L': 30, '00685L': 20 };
  const lev2 = (v631: number, v685: number) => ({
    lev2: v631 + v685,
    lev2Holdings: [
      ...(v631 > 0 ? [{ symbol: '00631L' as const, value: v631, shares: v631 / 30 }] : []),
      ...(v685 > 0 ? [{ symbol: '00685L' as const, value: v685, shares: v685 / 20 }] : []),
    ],
  });
  const hold = (v50: number, v631: number, v685: number, cash: number, others = 0, factor = 1) =>
    ({
      '0050': v50,
      ...lev2(v631, v685),
      cash,
      othersValue: others,
      othersExposure: others * factor,
    }) satisfies ScopeHoldings;

  it('S4 兩檔正 2 都持有、過度曝險：兩檔各賣 22,000 股、0050 買 13,333 股；執行後為「不動」', () => {
    const advice = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      hold(0.5 * M, 3 * M, 2 * M, 1 * M),
      prices,
    );
    expect(advice.scopeAssets).toBe(6.5 * M);
    expect(advice.eActual).toBeCloseTo(1.6154, 4);
    expect(advice.action).toBe('減碼');
    expect(advice.orders).toEqual([
      { symbol: '00631L', shares: -22_000 },
      { symbol: '00685L', shares: -22_000 },
      { symbol: '0050', shares: 13_333 },
    ]);
    expect(advice.lev2Change).toBeCloseTo(-1_100_000, 6); // 類別一個結論、一個金額
    expect(advice.cashChange).toBeCloseTo(300_020, 6);
    expect(advice.targets['0050']).toBeCloseTo(1.3 * M, 6);
    expect(advice.targets.lev2).toBeCloseTo(3.9 * M, 6);
    expect(advice.targets.cash).toBeCloseTo(1.3 * M, 6);

    // 執行後（忽略取整）再算：不動
    const again = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      hold(1.3 * M, 2.34 * M, 1.56 * M, 1.3 * M),
      prices,
    );
    expect(again.action).toBe('不動');
    expect(again.orders).toEqual([]);
  });

  it('S5 沒持有任何正 2：預設買 00631L 120,000 股、0050 買 3,333 股；執行後為「不動」', () => {
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, hold(1 * M, 0, 0, 5 * M), prices);
    expect(advice.action).toBe('加碼');
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: 3_333 },
      { symbol: '00631L', shares: 120_000 },
    ]);
    expect(advice.lev2Change).toBeCloseTo(3_600_000, 6);
    expect(advice.cashChange).toBeCloseTo(-3_799_980, 6);

    const again = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      hold(1.2 * M, 3.6 * M, 0, 1.2 * M),
      prices,
    );
    expect(again.action).toBe('不動');
  });

  it('S6 只持有 00685L：全給那一檔，賣 55,000 股；0050 買 13,333 股', () => {
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, hold(0.5 * M, 0, 5 * M, 1 * M), prices);
    expect(advice.action).toBe('減碼');
    expect(advice.orders).toEqual([
      { symbol: '00685L', shares: -55_000 },
      { symbol: '0050', shares: 13_333 },
    ]);
    expect(advice.orders.some((o) => o.symbol === '00631L')).toBe(false);
  });

  it('S7 Boss 的持股（含 NTSD 與 00685L）：A ＝ 7.5M、E ＝ 1.1333 ＝ 水位，加碼一個結論', () => {
    // 0050 1.0／00631L 1.5／00685L 1.0／現金類 2.0／其他一倍 1.0／NTSD 1.0（1.5 倍）
    const holdings = {
      '0050': 1 * M,
      ...lev2(1.5 * M, 1 * M),
      cash: 2 * M,
      othersValue: 2 * M, // 其他一倍 1.0 ＋ NTSD 1.0
      othersExposure: 1 * M + 1.5 * M,
    } satisfies ScopeHoldings;
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, holdings, prices);
    expect(advice.scopeAssets).toBe(7.5 * M);
    // 水位 ＝ (1.0 + 2×2.5 + 1.0 + 1.5) ÷ 7.5
    expect(advice.eActual).toBeCloseTo((1 + 5 + 1 + 1.5) / 7.5, 12);
    expect(advice.eActual).toBeCloseTo(1.1333, 4);
    expect(advice.eReach).toBeCloseTo(1.4, 12);
    expect(advice.capped).toBe(false);
    expect(advice.aboveTarget).toBe(false);
    expect(advice.targets['0050']).toBeCloseTo(0, 6);
    expect(advice.targets.lev2).toBeCloseTo(4 * M, 6);
    expect(advice.targets.cash).toBeCloseTo(1.5 * M, 6);
    expect(advice.action).toBe('加碼');
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: -16_667 },
      { symbol: '00631L', shares: 30_000 },
      { symbol: '00685L', shares: 30_000 },
    ]);
    expect(advice.cashChange).toBeCloseTo(-499_980, 6);

    // 執行後（忽略取整）再算：不動
    const after = {
      '0050': 0,
      ...lev2(2.4 * M, 1.6 * M),
      cash: 1.5 * M,
      othersValue: 2 * M,
      othersExposure: 2.5 * M,
    } satisfies ScopeHoldings;
    expect(advise('OFFENSE', 'OFFENSE', BALANCED, after, prices).action).toBe('不動');
  });

  it('類別層級只給一個方向：兩檔不會一檔加碼、另一檔減碼', () => {
    const cases = [
      hold(0.5 * M, 3 * M, 2 * M, 1 * M),
      hold(0.5 * M, 0.2 * M, 4 * M, 1 * M),
      hold(1 * M, 0.3 * M, 0.1 * M, 5 * M),
      hold(0, 1 * M, 1 * M, 0),
    ];
    for (const holdings of cases) {
      for (const state of ['OFFENSE', 'DEFENSE'] as const) {
        const orders = advise('DEFENSE', state, BALANCED, holdings, prices).orders.filter(
          (o) => o.symbol !== '0050',
        );
        expect(new Set(orders.map((o) => Math.sign(o.shares))).size).toBeLessThanOrEqual(1);
      }
    }
  });

  it('進入防守：兩檔全部賣出，不會賣超過持有股數', () => {
    const holdings = hold(0.5 * M, 3 * M, 2 * M, 1 * M);
    const advice = advise('OFFENSE', 'DEFENSE', BALANCED, holdings, prices);
    expect(advice.orders).toEqual([
      { symbol: '0050', shares: -8_333 },
      { symbol: '00631L', shares: -100_000 },
      { symbol: '00685L', shares: -100_000 },
    ]);
    expect(advice.targets.lev2).toBe(0);
  });

  it('價格缺失的正 2 成員不產生 order，且不影響其他標的', () => {
    const advice = advise('OFFENSE', 'OFFENSE', BALANCED, hold(0.5 * M, 3 * M, 2 * M, 1 * M), {
      '0050': 60,
      '00631L': 30,
    });
    expect(advice.orders.map((o) => o.symbol).sort()).toEqual(['0050', '00631L']);
    expect(Number.isFinite(advice.cashChange)).toBe(true);
  });

  it('未提供 lev2Holdings（退化）：視為全部持有在 00631L，與只有 00631L 時相同', () => {
    const legacy = advise('OFFENSE', 'OFFENSE', BALANCED, scope(0, 765_000, 235_000), {
      '0050': 60,
      '00631L': 100,
    });
    const explicit = advise(
      'OFFENSE',
      'OFFENSE',
      BALANCED,
      {
        ...scope(0, 765_000, 235_000),
        lev2Holdings: [{ symbol: '00631L', value: 765_000, shares: 7_650 }],
      },
      { '0050': 60, '00631L': 100 },
    );
    expect(legacy.orders).toEqual(explicit.orders);
    expect(legacy.cashChange).toBe(explicit.cashChange);
  });
});

describe('brake settings (localStorage)', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('沒存過 → 預設（平衡、防守 E=0）', () => {
    expect(loadBrakeSettings()).toEqual(DEFAULT_BRAKE_SETTINGS);
  });

  it('存了再讀回來', () => {
    saveBrakeSettings({ profile: 'aggressive', defenseE: 0.3, minCashRatio: 0.15 });
    expect(loadBrakeSettings()).toEqual({
      profile: 'aggressive',
      defenseE: 0.3,
      minCashRatio: 0.15,
    });
  });

  it('寫在 portfolio.drawdownBrake 這個 key', () => {
    saveBrakeSettings({ ...DEFAULT_BRAKE_SETTINGS, defenseE: 0.3 });
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

  it('未知的風險等級、超出 0~0.5 的 k 逐欄退回預設', () => {
    const s = sanitizeBrakeSettings({ profile: 'yolo', minCashRatio: 0.9 });
    expect(s).toEqual(DEFAULT_BRAKE_SETTINGS);
    expect(sanitizeBrakeSettings({ minCashRatio: 'x' })).toEqual(DEFAULT_BRAKE_SETTINGS);
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
