import { describe, expect, it } from 'vitest';

import { advise, BrakeParams } from './drawdownBrake';
import {
  classifyPositions,
  CORE_FACTORS,
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

describe('STRATEGY_EXCLUDED', () => {
  it('目前只有美股 NTSD（DECISION-018）；這張表是唯一決定誰被排除的地方', () => {
    expect(STRATEGY_EXCLUDED).toEqual([{ market: 'US', symbol: 'NTSD' }]);
    expect(isExcluded(ntsd(1))).toBe(true);
    // 同代號但不是美股 → 不排除
    expect(isExcluded(pos({ market: 'TW', symbol: 'NTSD' }))).toBe(false);
    expect(isExcluded(other('NTSX', 1, 0.9))).toBe(false);
  });
});

describe('classifyPositions — 分桶', () => {
  it('依序判定：核心 → 倍數 0 現金類 → 倍數 1 其他一倍 → 其餘其他倍數；NTSD 不在任何一桶', () => {
    const c = classifyPositions([
      p0050(400),
      p00631L(250),
      cash(200),
      other('00679B', 30, 0), // 債券 ETF，倍數 0 → 現金類
      other('2330', 100),
      other('VT', 50, 1, { market: 'US', currency: 'USD' }),
      other('TQQQ', 20, 3, { market: 'US', currency: 'USD' }),
      other('00632R', 10, -1),
      ntsd(100),
    ]);
    expect(c.core0050).toMatchObject({ value: 400, exposure: 400, count: 1 });
    expect(c.core00631L).toMatchObject({ value: 250, exposure: 500, count: 1 });
    expect(c.cash).toMatchObject({ value: 230, exposure: 0, count: 2 });
    expect(c.otherOne).toMatchObject({ value: 150, exposure: 150, count: 2 });
    expect(c.otherMulti).toMatchObject({ value: 30, exposure: 60 - 10, count: 2 });
    expect(c.othersValue).toBe(180);
    expect(c.othersExposure).toBe(200);
    expect(c.excluded).toHaveLength(1);
    expect(c.excludedValue).toBe(100);
  });

  it('各桶市值合計 ＝ 策略範圍資產 ＝ 流動總資產 − 被排除持股（A21 對帳）', () => {
    const positions = [
      p0050(400),
      p00631L(250),
      cash(200),
      other('2330', 100),
      other('00632R', 10, -1),
      ntsd(100),
    ];
    const liquidGross = positions.reduce((sum, p) => sum + p.marketValueTWD, 0); // 1,060
    const c = classifyPositions(positions);
    const bucketSum =
      c.core0050.value + c.core00631L.value + c.cash.value + c.otherOne.value + c.otherMulti.value;
    expect(bucketSum).toBe(c.scopeAssets);
    expect(c.scopeAssets).toBe(liquidGross - c.excludedValue);
    expect(c.scopeAssets).toBe(960);
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
    for (const bucket of [c.core0050, c.core00631L, c.otherOne, c.otherMulti, c.cash]) {
      expect(bucket.count).toBe(0);
    }
    expect(Number.isNaN(c.scopeExposure)).toBe(false);
  });

  it('只有 NTSD → 範圍資產 0，不會有建議', () => {
    const c = classifyPositions([ntsd(500)]);
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
  it('範圍內價格取得失敗的持股被列出（含核心）；被排除的不算', () => {
    const c = classifyPositions([
      p0050(100),
      other('AAPL', 0, 1, { market: 'US', currency: 'USD', priceFetchFailed: true }),
      { ...p00631L(0), priceFetchFailed: true },
      { ...ntsd(0), priceFetchFailed: true },
    ]);
    expect(c.priceFailed.map((p) => p.symbol)).toEqual(['AAPL', '00631L']);
  });

  it('核心倍數與策略定義不符（0050 ≠ 1、00631L ≠ 2）→ 標記資料異常', () => {
    expect(CORE_FACTORS).toEqual({ '0050': 1, '00631L': 2 });
    expect(classifyPositions([p0050(100), p00631L(100)]).coreFactorAnomaly).toBeNull();
    expect(classifyPositions([{ ...p00631L(100), exposureFactor: 1 }]).coreFactorAnomaly).toBe(
      '00631L',
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

  it('沒有被排除持股：卡片 E 等於水位（差異僅四捨五入）', () => {
    const positions = [
      p0050(4_000_000),
      p00631L(2_500_000),
      cash(2_000_000),
      other('2330', 1_500_000),
    ];
    const c = classifyPositions(positions);
    const w = water(positions);
    expect(c.scopeExposure / c.scopeAssets).toBeCloseTo(w.exposure / w.gross, 12);
    expect(Math.abs((c.scopeExposure / c.scopeAssets) * 100 - w.pct)).toBeLessThanOrEqual(0.05);
  });

  it('加 NTSD 後：E ＝ (marketExposure − 1.5×NTSD) ÷ (liquidGross − NTSD)', () => {
    const base = [p0050(4_000_000), p00631L(2_500_000), cash(2_000_000), other('2330', 1_500_000)];
    const positions = [...base, ntsd(1_000_000)];
    const c = classifyPositions(positions);
    const w = water(positions);
    expect(c.scopeExposure / c.scopeAssets).toBeCloseTo(
      (w.exposure - 1.5 * 1_000_000) / (w.gross - 1_000_000),
      12,
    );
    expect(c.scopeAssets).toBe(10_000_000);
    expect(c.scopeExposure / c.scopeAssets).toBeCloseTo(1.05, 12);
  });

  it('改 NTSD 金額只改被排除的金額，不改 E、桶與建議', () => {
    const base = [p0050(4_000_000), p00631L(2_500_000), cash(2_000_000), other('2330', 1_500_000)];
    const params: BrakeParams = { eOn: 1.4, eOff: 0, k: 0.2 };
    const prices = { '0050': 60, '00631L': 30 };
    const results = [1_000_000, 5_000_000, 0].map((n) => {
      const c = classifyPositions(n > 0 ? [...base, ntsd(n)] : base);
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
      '00631L': 200,
      cash: 300,
      othersValue: 450,
      othersExposure: 400 + 150,
    });
  });
});
