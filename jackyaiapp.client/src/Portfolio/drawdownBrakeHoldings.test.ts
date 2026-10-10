import { describe, expect, it } from 'vitest';

import {
  daysBetween,
  extractStrategyPositions,
  findSplitGuardHit,
  isSnapshotStale,
  splitGuardMessage,
  SPLIT_EVENTS,
  STALE_SNAPSHOT_DAYS,
  sumTwdCash,
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
});

describe('extractStrategyPositions', () => {
  it('只取台股 STOCK 的 0050 / 00631L，用 quantity 與 currentPrice', () => {
    const held = extractStrategyPositions([
      pos({ symbol: '0050', quantity: 2000, currentPrice: 100 }),
      pos({ symbol: '00631L', quantity: 11_800, currentPrice: 50 }),
      pos({ symbol: '2330', quantity: 1000, currentPrice: 900 }),
      pos({ assetType: 'FUND', symbol: '0050' }),
      pos({ market: 'US', symbol: '0050' }),
    ]);
    expect(held).toEqual([
      { symbol: '0050', quantity: 2000, price: 100 },
      { symbol: '00631L', quantity: 11_800, price: 50 },
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

describe('sumTwdCash', () => {
  it('只加總 TWD 現金部位', () => {
    expect(
      sumTwdCash([
        pos({ assetType: 'CASH', symbol: 'TWD', currency: 'TWD', marketValueTWD: 300_000 }),
        pos({ assetType: 'CASH', symbol: 'TWD2', currency: 'TWD', marketValueTWD: 50_000 }),
        pos({ assetType: 'CASH', symbol: 'USD', currency: 'USD', marketValueTWD: 999_999 }),
        pos({ marketValueTWD: 60_000 }),
      ]),
    ).toBe(350_000);
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

  it('沒持有該標的（只有策略現金）→ 不因它而擋，即使快照很舊', () => {
    expect(findSplitGuardHit([], '2020-01-01')).toBeNull();
    expect(findSplitGuardHit([], null)).toBeNull();
  });

  it('持有分割標的卻沒有快照日期 → 無法確認，保守擋下', () => {
    expect(findSplitGuardHit(held0050, null)?.symbol).toBe('0050');
    expect(findSplitGuardHit(held0050, undefined)?.symbol).toBe('0050');
  });

  it('提示文字', () => {
    expect(splitGuardMessage({ symbol: '0050', event: SPLIT_EVENTS['0050'] })).toBe(
      '你的持股快照建立於 0050 分割（2025-06-18 1 拆 4）之前，股數可能未調整，請先更新持股',
    );
    expect(splitGuardMessage({ symbol: '00631L', event: SPLIT_EVENTS['00631L'] })).toBe(
      '你的持股快照建立於 00631L 分割（2026-03-31 1 拆 22）之前，股數可能未調整，請先更新持股',
    );
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
