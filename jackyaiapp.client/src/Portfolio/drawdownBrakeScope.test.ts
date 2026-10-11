import { describe, expect, it } from 'vitest';

import { advise, BrakeParams } from './drawdownBrake';
import {
  classifyPositions,
  CORE_FACTORS,
  CORE_LEVERAGED_2X,
  CORE_SYMBOLS,
  ExcludedInstrument,
  isExcluded,
  STRATEGY_EXCLUDED,
  toScopeHoldings,
} from './drawdownBrakeScope';

import { PositionDto } from '@/apis/portfolioApis/types';

function pos(overrides: Partial<PositionDto>): PositionDto {
  const base: PositionDto = {
    assetType: 'STOCK',
    symbol: '2330',
    name: '台積電',
    market: 'TW',
    currency: 'TWD',
    quantity: 1000,
    currentPrice: 100,
    marketValueLocal: 100_000,
    marketValueTWD: 100_000,
    exposureFactor: 1,
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
  };
  return { ...base, ...overrides };
}

const p0050 = (value: number) => pos({ symbol: '0050', name: '元大台灣50', marketValueTWD: value });
const p00631L = (value: number) =>
  pos({ symbol: '00631L', name: '元大台灣50正2', exposureFactor: 2, marketValueTWD: value });
const p00685L = (value: number) =>
  pos({ symbol: '00685L', name: '群益臺灣加權正2', exposureFactor: 2, marketValueTWD: value });
const cash = (value: number, overrides: Partial<PositionDto> = {}) =>
  pos({
    assetType: 'CASH',
    symbol: 'TWD',
    name: '現金',
    exposureFactor: 0,
    quantity: value,
    marketValueTWD: value,
    ...overrides,
  });
const ntsd = (value: number) =>
  pos({
    market: 'US',
    currency: 'USD',
    symbol: 'NTSD',
    name: 'WisdomTree Efficient U.S. Plus International Equity Fund',
    exposureFactor: 1.5,
    marketValueTWD: value,
  });
const other = (symbol: string, value: number, factor = 1, overrides: Partial<PositionDto> = {}) =>
  pos({ symbol, name: symbol, exposureFactor: factor, marketValueTWD: value, ...overrides });

/** 假的排除表：機制保留（DECISION-021 之後 `STRATEGY_EXCLUDED` 是空表），用它驗證排除與對帳行。 */
const FAKE_EXCLUDED: ExcludedInstrument[] = [{ market: 'US', symbol: 'NTSD' }];

describe('STRATEGY_EXCLUDED', () => {
  it('目前是空表（DECISION-021：NTSD 納入曝險）；沒有任何持股被排除', () => {
    expect(STRATEGY_EXCLUDED).toEqual([]);
    expect(isExcluded(ntsd(1))).toBe(false);
    expect(isExcluded(p0050(1))).toBe(false);
  });

  it('排除機制仍然有效：以排除表（市場＋代號）判斷，同代號不同市場不排除', () => {
    expect(isExcluded(ntsd(1), FAKE_EXCLUDED)).toBe(true);
    expect(isExcluded(pos({ market: 'TW', symbol: 'NTSD' }), FAKE_EXCLUDED)).toBe(false);
    expect(isExcluded(other('NTSX', 1, 0.9), FAKE_EXCLUDED)).toBe(false);
  });
});

describe('CORE_LEVERAGED_2X — 台股正 2 類成員表', () => {
  it('唯一成員表：00631L、00685L；核心標的 ＝ 0050 ＋ 成員', () => {
    expect([...CORE_LEVERAGED_2X]).toEqual(['00631L', '00685L']);
    expect([...CORE_SYMBOLS]).toEqual(['0050', '00631L', '00685L']);
  });

  it('核心倍數：0050 ＝ 1、成員 ＝ 2（由成員表產生，不另寫一份）', () => {
    expect(CORE_FACTORS).toEqual({ '0050': 1, '00631L': 2, '00685L': 2 });
  });
});

describe('classifyPositions — 分桶', () => {
  const mixed = () => [
    p0050(400),
    p00631L(250),
    p00685L(150),
    cash(200),
    other('00679B', 30, 0), // 債券 ETF，倍數 0 → 現金類
    other('2330', 100),
    other('VT', 50, 1, { market: 'US', currency: 'USD' }),
    other('TQQQ', 20, 3, { market: 'US', currency: 'USD' }),
    other('00632R', 10, -1),
    ntsd(100),
  ];

  it('依序判定：核心 → 倍數 0 現金類 → 倍數 1 其他一倍 → 其餘其他倍數；NTSD 落入其他倍數', () => {
    const c = classifyPositions(mixed());
    expect(c.core0050).toMatchObject({ value: 400, exposure: 400, count: 1 });
    expect(c.coreLev2).toMatchObject({ value: 400, exposure: 800, count: 2 });
    expect(c.cash).toMatchObject({ value: 230, exposure: 0, count: 2 });
    expect(c.otherOne).toMatchObject({ value: 150, exposure: 150, count: 2 });
    // TQQQ 20（3 倍）＋ 00632R 10（−1 倍）＋ NTSD 100（1.5 倍）
    expect(c.otherMulti).toMatchObject({ value: 130, exposure: 60 - 10 + 150, count: 3 });
    expect(c.othersValue).toBe(280);
    expect(c.othersExposure).toBe(350);
    expect(c.excluded).toEqual([]);
    expect(c.excludedValue).toBe(0);
  });

  it('各桶市值合計 ＝ 策略範圍資產 ＝ 流動總資產（沒有被排除持股，A21 對帳）', () => {
    const positions = mixed();
    const liquidGross = positions.reduce((sum, p) => sum + p.marketValueTWD, 0); // 1,310
    const c = classifyPositions(positions);
    const bucketSum =
      c.core0050.value + c.coreLev2.value + c.cash.value + c.otherOne.value + c.otherMulti.value;
    expect(bucketSum).toBe(c.scopeAssets);
    expect(c.scopeAssets).toBe(liquidGross);
    expect(c.scopeAssets).toBe(1_310);
  });

  it('排除機制（假的排除表）：被排除的持股不在任何一桶，範圍資產 ＝ 流動總資產 − 被排除持股', () => {
    const positions = mixed();
    const liquidGross = positions.reduce((sum, p) => sum + p.marketValueTWD, 0);
    const c = classifyPositions(positions, FAKE_EXCLUDED);
    expect(c.excluded.map((p) => p.symbol)).toEqual(['NTSD']);
    expect(c.excludedValue).toBe(100);
    expect(c.otherMulti).toMatchObject({ value: 30, exposure: 60 - 10, count: 2 });
    expect(c.scopeAssets).toBe(liquidGross - c.excludedValue);
    expect(c.scopeAssets).toBe(1_210);
  });

  it('不動產與負債不在流動資產，一律略過', () => {
    const c = classifyPositions([
      p0050(100),
      pos({ assetType: 'REAL_ESTATE', symbol: 'HOME', exposureFactor: 0, marketValueTWD: 9_000 }),
      pos({ assetType: 'LIABILITY', symbol: 'LOAN', exposureFactor: 0, marketValueTWD: -5_000 }),
    ]);
    expect(c.scopeAssets).toBe(100);
    expect(c.cash.count).toBe(0);
  });

  it('外幣現金也是現金類', () => {
    const c = classifyPositions([cash(320, { symbol: 'USD', currency: 'USD', quantity: 10 })]);
    expect(c.cash).toMatchObject({ value: 320, count: 1 });
  });

  it('同一標的多列加總；數量 0 的股票列不算持股', () => {
    const c = classifyPositions([
      p0050(100),
      p0050(50),
      pos({ symbol: '2330', quantity: 0, marketValueTWD: 0 }),
    ]);
    expect(c.core0050).toMatchObject({ value: 150, count: 2 });
    expect(c.otherOne.count).toBe(0);
  });

  it('沒有的桶 count 為 0（UI 不顯示空列）；沒有任何持股 → 範圍資產 0', () => {
    const c = classifyPositions([]);
    expect(c.scopeAssets).toBe(0);
    for (const bucket of [c.core0050, c.coreLev2, c.otherOne, c.otherMulti, c.cash]) {
      expect(bucket.count).toBe(0);
    }
    expect(c.lev2Holdings).toEqual([]);
    expect(Number.isNaN(c.scopeExposure)).toBe(false);
  });

  it('只有 NTSD → 計入其他倍數商品與曝險（1.5 倍），不是核心', () => {
    const c = classifyPositions([ntsd(500)]);
    expect(c.scopeAssets).toBe(500);
    expect(c.otherMulti).toMatchObject({ value: 500, exposure: 750, count: 1 });
    expect(c.excluded).toEqual([]);
  });

  it('只有被排除的持股（假的排除表）→ 範圍資產 0', () => {
    const c = classifyPositions([ntsd(500)], FAKE_EXCLUDED);
    expect(c.scopeAssets).toBe(0);
    expect(c.excludedValue).toBe(500);
  });

  it('曝險只用 DTO 的 exposureFactor，前端不另寫倍數規則', () => {
    // 名稱看起來像槓桿，但 DTO 給 1 → 就是 1 倍
    const c = classifyPositions([other('0000L', 100, 1, { name: '某某正2' })]);
    expect(c.otherOne.exposure).toBe(100);
    expect(c.otherMulti.count).toBe(0);
  });
});

describe('classifyPositions — 資料防護', () => {
  it('範圍內價格取得失敗的持股被列出（含核心與 NTSD）；被排除的不算', () => {
    const positions = [
      p0050(100),
      other('AAPL', 0, 1, { market: 'US', currency: 'USD', priceFetchFailed: true }),
      { ...p00631L(0), priceFetchFailed: true },
      { ...ntsd(0), priceFetchFailed: true },
    ];
    expect(classifyPositions(positions).priceFailed.map((p) => p.symbol)).toEqual([
      'AAPL',
      '00631L',
      'NTSD',
    ]);
    expect(classifyPositions(positions, FAKE_EXCLUDED).priceFailed.map((p) => p.symbol)).toEqual([
      'AAPL',
      '00631L',
    ]);
  });

  it('核心倍數與策略定義不符（0050 ≠ 1、00631L／00685L ≠ 2）→ 標記資料異常', () => {
    expect(
      classifyPositions([p0050(100), p00631L(100), p00685L(100)]).coreFactorAnomaly,
    ).toBeNull();
    expect(classifyPositions([{ ...p00631L(100), exposureFactor: 1 }]).coreFactorAnomaly).toBe(
      '00631L',
    );
    expect(classifyPositions([{ ...p00685L(100), exposureFactor: 1 }]).coreFactorAnomaly).toBe(
      '00685L',
    );
    expect(classifyPositions([{ ...p0050(100), exposureFactor: 2 }]).coreFactorAnomaly).toBe(
      '0050',
    );
  });

  it('非台股 STOCK 的 0050 代號不算核心', () => {
    const c = classifyPositions([pos({ symbol: '0050', market: 'US', marketValueTWD: 10 })]);
    expect(c.core0050.count).toBe(0);
    expect(c.otherOne.count).toBe(1);
  });
});

describe('12a 曝險與「水位」對得起來', () => {
  // 水位 ＝ marketExposureTWD ÷ liquidGrossAssetsTWD（後端同一套 exposureFactor）
  const water = (positions: PositionDto[]) => {
    const live = positions.filter(
      (p) => p.assetType !== 'REAL_ESTATE' && p.assetType !== 'LIABILITY',
    );
    const gross = live.reduce((s, p) => s + p.marketValueTWD, 0);
    const exposure = live.reduce((s, p) => s + p.marketValueTWD * p.exposureFactor, 0);
    return { gross, exposure, pct: Math.round((exposure / gross) * 1000) / 10 };
  };
  const base = () => [
    p0050(4_000_000),
    p00631L(2_500_000),
    cash(2_000_000),
    other('2330', 1_500_000),
  ];

  it('沒有 NTSD：卡片 E 等於水位（差異僅四捨五入）', () => {
    const positions = base();
    const c = classifyPositions(positions);
    const w = water(positions);
    expect(c.scopeExposure / c.scopeAssets).toBeCloseTo(w.exposure / w.gross, 12);
    expect(Math.abs((c.scopeExposure / c.scopeAssets) * 100 - w.pct)).toBeLessThanOrEqual(0.05);
  });

  it('含 NTSD（1.5 倍）與 00685L：卡片 E 仍等於水位，沒有被排除持股', () => {
    const positions = [...base(), p00685L(1_000_000), ntsd(1_000_000)];
    const c = classifyPositions(positions);
    const w = water(positions);
    expect(c.excluded).toEqual([]);
    expect(c.scopeAssets).toBe(w.gross);
    expect(c.scopeExposure / c.scopeAssets).toBeCloseTo(w.exposure / w.gross, 12);
    expect(Math.abs((c.scopeExposure / c.scopeAssets) * 100 - w.pct)).toBeLessThanOrEqual(0.05);
    // (4.0 + 2×2.5 + 0 + 1.5 + 2×1.0 + 1.5×1.0) ÷ 12.0
    expect(c.scopeExposure / c.scopeAssets).toBeCloseTo(14 / 12, 12);
  });

  it('改 NTSD 金額會改變 E、其他倍數桶與核心建議（與其他持股一致），但 NTSD 本身沒有 order', () => {
    const params: BrakeParams = { eOn: 1.4, eOff: 0, k: 0.2 };
    const prices = { '0050': 60, '00631L': 30 };
    const advices = [0, 1_000_000, 5_000_000].map((n) => {
      const positions = n > 0 ? [...base(), ntsd(n)] : base();
      const c = classifyPositions(positions);
      return { c, advice: advise('DEFENSE', 'OFFENSE', params, toScopeHoldings(c), prices) };
    });
    const [none, small, large] = advices;
    expect(small.c.scopeAssets).toBe(none.c.scopeAssets + 1_000_000);
    expect(large.c.otherMulti.value).toBe(5_000_000);
    expect(small.advice.eActual).not.toBeCloseTo(none.advice.eActual, 6);
    expect(large.advice.eActual).not.toBeCloseTo(small.advice.eActual, 6);
    expect(large.advice.targets['0050']).not.toBeCloseTo(small.advice.targets['0050'], 6);
    for (const { advice } of advices) {
      for (const order of advice.orders) expect(order.symbol).not.toBe('NTSD');
    }
  });

  it('排除機制（假的排除表）：改被排除持股的金額只改 excludedValue，不改 E、桶與建議', () => {
    const params: BrakeParams = { eOn: 1.4, eOff: 0, k: 0.2 };
    const prices = { '0050': 60, '00631L': 30 };
    const results = [1_000_000, 5_000_000, 0].map((n) => {
      const c = classifyPositions(n > 0 ? [...base(), ntsd(n)] : base(), FAKE_EXCLUDED);
      return {
        c,
        advice: advise('DEFENSE', 'OFFENSE', params, toScopeHoldings(c), prices),
      };
    });
    const [first, ...rest] = results;
    for (const r of rest) {
      expect(r.c.scopeAssets).toBe(first.c.scopeAssets);
      expect(r.c.scopeExposure).toBe(first.c.scopeExposure);
      expect(r.c.core0050.value).toBe(first.c.core0050.value);
      expect(r.advice.eActual).toBe(first.advice.eActual);
      expect(r.advice.orders).toEqual(first.advice.orders);
    }
    expect(results.map((r) => r.c.excludedValue)).toEqual([1_000_000, 5_000_000, 0]);
  });
});

describe('台股正 2 類（lev2）分桶', () => {
  it('00631L 與 00685L 合併成一個桶，曝險倍數 2，各檔明細另列（同一標的多列加總）', () => {
    const c = classifyPositions([
      p00631L(300),
      { ...p00631L(100), quantity: 50 },
      { ...p00685L(200), quantity: 400 },
    ]);
    expect(c.coreLev2).toMatchObject({ value: 600, exposure: 1_200, count: 3 });
    expect(c.lev2Holdings).toEqual([
      { symbol: '00631L', value: 400, shares: 1_050 },
      { symbol: '00685L', value: 200, shares: 400 },
    ]);
    expect(c.otherMulti.count).toBe(0);
  });

  it('明細只列有持有的檔，順序依成員表（不依持股順序）', () => {
    expect(classifyPositions([p00685L(10)]).lev2Holdings.map((h) => h.symbol)).toEqual(['00685L']);
    expect(classifyPositions([p00685L(10), p00631L(10)]).lev2Holdings.map((h) => h.symbol)).toEqual(
      ['00631L', '00685L'],
    );
    expect(classifyPositions([p0050(10)]).lev2Holdings).toEqual([]);
  });

  it('只有成員表上的標的算正 2：其他倍數 2 的商品（例如 00675L）歸其他倍數商品，不給建議', () => {
    const c = classifyPositions([other('00675L', 100, 2, { name: '富邦臺灣加權正2' })]);
    expect(c.coreLev2.count).toBe(0);
    expect(c.otherMulti).toMatchObject({ value: 100, exposure: 200, count: 1 });
    expect(c.lev2Holdings).toEqual([]);
  });

  it('非台股 STOCK 的 00685L 代號不算正 2', () => {
    const c = classifyPositions([pos({ symbol: '00685L', market: 'US', exposureFactor: 2 })]);
    expect(c.coreLev2.count).toBe(0);
    expect(c.otherMulti.count).toBe(1);
  });

  it('數量 0 的 00685L 不算持有', () => {
    const c = classifyPositions([{ ...p00685L(0), quantity: 0 }, p00631L(100)]);
    expect(c.lev2Holdings.map((h) => h.symbol)).toEqual(['00631L']);
    expect(c.coreLev2.count).toBe(1);
  });
});

describe('toScopeHoldings', () => {
  it('分桶 → advise 的輸入', () => {
    const c = classifyPositions([
      p0050(100),
      p00631L(200),
      cash(300),
      other('2330', 400),
      other('TQQQ', 50, 3),
    ]);
    expect(toScopeHoldings(c)).toEqual({
      '0050': 100,
      lev2: 200,
      cash: 300,
      othersValue: 450,
      othersExposure: 400 + 150,
      lev2Holdings: [{ symbol: '00631L', value: 200, shares: 1000 }],
    });
  });
});
