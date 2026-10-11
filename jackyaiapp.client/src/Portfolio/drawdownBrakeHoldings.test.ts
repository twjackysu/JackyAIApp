import { describe, expect, it } from 'vitest';

import {
  daysBetween,
  extractStrategyPositions,
  findSplitGuardHit,
  isSnapshotStale,
  MAX_FAILED_PRICE_REFRESH,
  priceRefreshSymbols,
  requiredPriceSymbols,
  splitGuardMessage,
  SPLIT_EVENTS,
  SplitEvent,
  STALE_SNAPSHOT_DAYS,
} from './drawdownBrakeHoldings';

import { PositionDto } from '@/apis/portfolioApis/types';

function pos(overrides: Partial<PositionDto>): PositionDto {
  return {
    assetType: 'STOCK',
    symbol: '0050',
    name: '元大台灣50',
    market: 'TW',
    currency: 'TWD',
    quantity: 1000,
    currentPrice: 60,
    marketValueLocal: 60_000,
    marketValueTWD: 60_000,
    exposureFactor: 1,
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
    ...overrides,
  };
}

describe('SPLIT_EVENTS', () => {
  it('來源 Boss #431：0050 2025-06-18 1 拆 4、00631L 2026-03-31 1 拆 22', () => {
    expect(SPLIT_EVENTS['0050']).toEqual({ date: '2025-06-18', ratio: 4 });
    expect(SPLIT_EVENTS['00631L']).toEqual({ date: '2026-03-31', ratio: 22 });
  });

  it('00685L 沒有條目：Boss 2026-10-11 確認無分割（不得自行猜測日期）', () => {
    expect(SPLIT_EVENTS['00685L']).toBeUndefined();
    expect(Object.keys(SPLIT_EVENTS).sort()).toEqual(['0050', '00631L']);
  });
});

describe('extractStrategyPositions', () => {
  it('只取台股 STOCK 的核心標的（0050 / 00631L / 00685L），用 quantity 與 currentPrice', () => {
    const held = extractStrategyPositions([
      pos({ symbol: '0050', quantity: 2000, currentPrice: 100 }),
      pos({ symbol: '00685L', quantity: 3_000, currentPrice: 20 }),
      pos({ symbol: '00631L', quantity: 11_800, currentPrice: 50 }),
      pos({ symbol: '2330', quantity: 1000, currentPrice: 900 }),
      pos({ symbol: '00675L', quantity: 1000, currentPrice: 40 }),
      pos({ assetType: 'FUND', symbol: '0050' }),
      pos({ market: 'US', symbol: '0050' }),
      pos({ market: 'US', symbol: '00685L' }),
    ]);
    // 順序依核心標的清單，不依持股順序；00675L（不在成員表）不算
    expect(held).toEqual([
      { symbol: '0050', quantity: 2000, price: 100 },
      { symbol: '00631L', quantity: 11_800, price: 50 },
      { symbol: '00685L', quantity: 3_000, price: 20 },
    ]);
  });

  it('同一標的多列加總；數量 0 視為沒持有', () => {
    const held = extractStrategyPositions([
      pos({ symbol: '0050', quantity: 1000, currentPrice: 100 }),
      pos({ symbol: '0050', quantity: 500, currentPrice: 100 }),
      pos({ symbol: '00631L', quantity: 0 }),
    ]);
    expect(held).toEqual([{ symbol: '0050', quantity: 1500, price: 100 }]);
  });

  it('價格抓取失敗或不是正數 → price 為 null（由呼叫端改用查價）', () => {
    expect(
      extractStrategyPositions([pos({ symbol: '0050', priceFetchFailed: true })])[0].price,
    ).toBeNull();
    expect(
      extractStrategyPositions([pos({ symbol: '00631L', currentPrice: 0 })])[0].price,
    ).toBeNull();
  });
});

describe('findSplitGuardHit (PM R2：分割硬性防護)', () => {
  const held0050 = [{ symbol: '0050' as const, quantity: 1000, price: 60 }];
  const held00631L = [{ symbol: '00631L' as const, quantity: 1000, price: 50 }];

  it('快照早於 0050 分割日 → 擋下', () => {
    expect(findSplitGuardHit(held0050, '2025-06-17')).toEqual({
      symbol: '0050',
      event: SPLIT_EVENTS['0050'],
    });
  });

  it('快照在分割日當天或之後 → 放行', () => {
    expect(findSplitGuardHit(held0050, '2025-06-18')).toBeNull();
    expect(findSplitGuardHit(held0050, '2025-06-19')).toBeNull();
    expect(findSplitGuardHit(held0050, '2026-10-09')).toBeNull();
  });

  it('00631L 以自己的分割日 2026-03-31 判斷', () => {
    expect(findSplitGuardHit(held00631L, '2026-03-30')?.symbol).toBe('00631L');
    expect(findSplitGuardHit(held00631L, '2026-03-31')).toBeNull();
  });

  it('0050 分割後、00631L 分割前的快照：只有持有 00631L 才被擋', () => {
    expect(findSplitGuardHit(held0050, '2026-01-10')).toBeNull();
    expect(findSplitGuardHit(held00631L, '2026-01-10')?.symbol).toBe('00631L');
    expect(findSplitGuardHit([...held0050, ...held00631L], '2026-01-10')?.symbol).toBe('00631L');
  });

  it('沒持有該標的（例如只有現金）→ 不因它而擋，即使快照很舊', () => {
    expect(findSplitGuardHit([], '2020-01-01')).toBeNull();
    expect(findSplitGuardHit([], null)).toBeNull();
  });

  it('持有分割標的卻沒有快照日期 → 無法確認，保守擋下', () => {
    expect(findSplitGuardHit(held0050, null)?.symbol).toBe('0050');
    expect(findSplitGuardHit(held0050, undefined)?.symbol).toBe('0050');
  });

  it('00685L 沒有分割條目：不因它而擋，即使快照很舊或沒有日期', () => {
    const held00685L = [{ symbol: '00685L' as const, quantity: 1000, price: 20 }];
    expect(findSplitGuardHit(held00685L, '2020-01-01')).toBeNull();
    expect(findSplitGuardHit(held00685L, null)).toBeNull();
    // 同時持有有條目的標的時，仍以那一檔判斷
    expect(findSplitGuardHit([...held00685L, ...held00631L], '2026-03-30')?.symbol).toBe('00631L');
  });

  it('提示文字', () => {
    expect(splitGuardMessage({ symbol: '0050', event: SPLIT_EVENTS['0050'] as SplitEvent })).toBe(
      '你的持股快照建立於 0050 分割（2025-06-18 1 拆 4）之前，股數可能未調整，請先更新持股',
    );
    expect(
      splitGuardMessage({ symbol: '00631L', event: SPLIT_EVENTS['00631L'] as SplitEvent }),
    ).toBe(
      '你的持股快照建立於 00631L 分割（2026-03-31 1 拆 22）之前，股數可能未調整，請先更新持股',
    );
  });
});

describe('requiredPriceSymbols — 算建議股數必須有價格的核心標的', () => {
  const held = (...symbols: Array<'0050' | '00631L' | '00685L'>) =>
    symbols.map((symbol) => ({ symbol, quantity: 1000, price: 50 }));

  it('一檔都沒持有：0050 與預設買進標的 00631L（00685L 不需要）', () => {
    expect(requiredPriceSymbols([])).toEqual(['0050', '00631L']);
  });

  it('持有的核心標的都要價格；持有任一檔正 2 時，沒持有的那檔不需要', () => {
    expect(requiredPriceSymbols(held('0050', '00631L', '00685L'))).toEqual([
      '0050',
      '00631L',
      '00685L',
    ]);
    expect(requiredPriceSymbols(held('0050', '00685L'))).toEqual(['0050', '00685L']);
    expect(requiredPriceSymbols(held('00631L'))).toEqual(['0050', '00631L']);
  });

  it('0050 一律需要（沒持有時也要查價，PM 假設 A4）', () => {
    for (const symbols of [[], ['00631L'], ['00685L']] as const) {
      expect(requiredPriceSymbols(held(...symbols))).toContain('0050');
    }
  });
});

describe('priceRefreshSymbols — 重新整理要重抓報價的代號', () => {
  const stockHeld = (symbol: string, extra: Partial<PositionDto> = {}) =>
    pos({ symbol, market: 'US', priceFetchFailed: true, ...extra });
  const core = [
    { symbol: '0050' as const, quantity: 1000, price: 60 },
    { symbol: '00685L' as const, quantity: 1000, price: 20 },
  ];

  it('沒有價格失敗的持股：只有持有的核心標的', () => {
    expect(priceRefreshSymbols(core, [])).toEqual(['0050', '00685L']);
    expect(priceRefreshSymbols([], [])).toEqual([]);
  });

  it('價格失敗的股票（例如 NTSD）一併重抓；核心標的不重複', () => {
    const failed = [stockHeld('NTSD'), pos({ symbol: '00685L', priceFetchFailed: true })];
    expect(priceRefreshSymbols(core, failed)).toEqual(['0050', '00685L', 'NTSD']);
  });

  it('基金不列（refresh-price 只處理股票）；同一代號只列一次', () => {
    const failed = [stockHeld('AAA', { assetType: 'FUND' }), stockHeld('NTSD'), stockHeld('NTSD')];
    expect(priceRefreshSymbols([], failed)).toEqual(['NTSD']);
  });

  it(`價格失敗者最多多重抓 ${MAX_FAILED_PRICE_REFRESH} 檔，核心標的不受限`, () => {
    const failed = Array.from({ length: 8 }, (_, i) => stockHeld(`S${i}`));
    const symbols = priceRefreshSymbols(core, failed);
    expect(symbols).toEqual(['0050', '00685L', 'S0', 'S1', 'S2', 'S3', 'S4']);
    expect(symbols).toHaveLength(core.length + MAX_FAILED_PRICE_REFRESH);
  });
});

describe('isSnapshotStale', () => {
  it(`距今 > ${STALE_SNAPSHOT_DAYS} 天才算過期（第 7 天還不算）`, () => {
    expect(isSnapshotStale('2026-10-03', '2026-10-10')).toBe(false);
    expect(isSnapshotStale('2026-10-02', '2026-10-10')).toBe(true);
    expect(isSnapshotStale('2026-10-10', '2026-10-10')).toBe(false);
  });

  it('沒有日期（無法判斷）不算過期', () => {
    expect(isSnapshotStale(null, '2026-10-10')).toBe(false);
    expect(isSnapshotStale(undefined, '2026-10-10')).toBe(false);
  });
});

describe('daysBetween', () => {
  it('跨月、跨年、跨夏令時間都以日曆天計', () => {
    expect(daysBetween('2026-09-30', '2026-10-01')).toBe(1);
    expect(daysBetween('2025-12-31', '2026-01-01')).toBe(1);
    expect(daysBetween('2026-03-07', '2026-03-09')).toBe(2);
    expect(daysBetween('2026-10-10', '2026-10-10')).toBe(0);
  });
});
