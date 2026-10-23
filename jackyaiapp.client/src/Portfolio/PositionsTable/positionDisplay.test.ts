import { describe, expect, it } from 'vitest';

import {
  exposureChipOf,
  groupPositions,
  isFlatValued,
  opensDetail,
  typeChipOf,
} from './positionDisplay';

import type { AssetType, PositionDto } from '@/apis/portfolioApis/types';

function holding(overrides: Partial<PositionDto> = {}): PositionDto {
  return {
    assetType: 'STOCK',
    symbol: '2330',
    name: '台積電',
    market: 'TW',
    currency: 'TWD',
    quantity: 1000,
    currentPrice: 600,
    marketValueLocal: 600_000,
    marketValueTWD: 600_000,
    exposureFactor: 1,
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
    ...overrides,
  };
}

describe('isFlatValued', () => {
  it('現金、負債、不動產是手動估值；股票、基金不是', () => {
    expect(
      (['CASH', 'LIABILITY', 'REAL_ESTATE', 'STOCK', 'FUND'] as AssetType[]).map((assetType) =>
        isFlatValued(holding({ assetType })),
      ),
    ).toEqual([true, true, true, false, false]);
  });
});

describe('typeChipOf', () => {
  it('股票顯示市場，沒有市場就是「股票」；其他類型各有標籤，不認得的類型當股票', () => {
    expect(typeChipOf(holding())).toEqual({ label: 'TW', color: 'primary' });
    expect(typeChipOf(holding({ market: 'US' }))).toEqual({ label: 'US', color: 'primary' });
    expect(typeChipOf(holding({ market: '' }))).toEqual({ label: '股票', color: 'primary' });
    expect(typeChipOf(holding({ assetType: 'FUND' }))).toEqual({ label: '基金', color: 'info' });
    expect(typeChipOf(holding({ assetType: 'CASH' }))).toEqual({ label: '現金', color: 'success' });
    expect(typeChipOf(holding({ assetType: 'LIABILITY' }))).toEqual({
      label: '負債',
      color: 'error',
    });
    expect(typeChipOf(holding({ assetType: 'REAL_ESTATE' }))).toEqual({
      label: '不動產',
      color: 'warning',
    });
    expect(typeChipOf(holding({ assetType: 'WEIRD' as AssetType, market: 'TW' }))).toEqual({
      label: '股票',
      color: 'primary',
    });
  });
});

describe('exposureChipOf', () => {
  it('1 倍沒有標籤；0 倍是「0曝險」；其他倍數（含反向）標出倍數', () => {
    expect(exposureChipOf(holding())).toBeNull();
    expect(exposureChipOf(holding({ exposureFactor: 0 }))).toEqual({
      label: '0曝險',
      color: 'default',
    });
    expect(exposureChipOf(holding({ exposureFactor: 2 }))).toEqual({
      label: '曝險×2',
      color: 'warning',
    });
    expect(exposureChipOf(holding({ exposureFactor: -1 }))?.label).toBe('曝險×-1');
  });

  it('手動估值的類別一律沒有曝險標籤', () => {
    expect(exposureChipOf(holding({ assetType: 'LIABILITY', exposureFactor: 2 }))).toBeNull();
    expect(exposureChipOf(holding({ assetType: 'CASH', exposureFactor: 0 }))).toBeNull();
  });
});

describe('opensDetail', () => {
  it('股票與有代號的基金可開明細，要有處理函式', () => {
    expect(opensDetail(holding(), true)).toBe(true);
    expect(opensDetail(holding({ assetType: 'FUND', symbol: 'F1' }), true)).toBe(true);
    expect(opensDetail(holding({ assetType: 'FUND', symbol: '' }), true)).toBe(false);
    expect(opensDetail(holding({ assetType: 'CASH' }), true)).toBe(false);
    expect(opensDetail(holding(), false)).toBe(false);
  });
});

describe('groupPositions', () => {
  it('依顯示順序分組、略過空的組，合計市值', () => {
    const groups = groupPositions([
      holding({ assetType: 'LIABILITY', marketValueTWD: -500 }),
      holding({ market: 'US', marketValueTWD: 200 }),
      holding({ marketValueTWD: 100 }),
      holding({ marketValueTWD: 50 }),
      holding({ assetType: 'WEIRD' as AssetType, marketValueTWD: 1 }),
    ]);
    expect(groups.map((g) => [g.key, g.label, g.positions.length, g.total])).toEqual([
      ['TW_STOCK', '台股', 2, 150],
      ['US_STOCK', '美股', 1, 200],
      ['LIABILITY', '負債', 1, -500],
      ['OTHER', '其他', 1, 1],
    ]);
  });
});
