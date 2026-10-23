import { describe, expect, it } from 'vitest';

import { SPLIT_EVENTS, SplitGuardHit } from '../drawdownBrakeHoldings';
import { classifyPositions } from '../drawdownBrakeScope';

import { noAdviceReason } from './noAdviceReason';

import { PositionDto } from '@/apis/portfolioApis/types';

function pos(overrides: Partial<PositionDto>): PositionDto {
  return {
    assetType: 'STOCK',
    symbol: '0050',
    name: '0050',
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
    ...overrides,
  };
}

const guardHit: SplitGuardHit = { symbol: '0050', event: SPLIT_EVENTS['0050']! };
const failedPrice = pos({
  symbol: 'AAPL',
  market: 'US',
  priceFetchFailed: true,
  marketValueTWD: 0,
});
const wrongFactor = pos({ symbol: '00685L', exposureFactor: 1 });
const usable = classifyPositions([pos({})]);
const unusable = classifyPositions([pos({}), failedPrice, wrongFactor]);
const empty = classifyPositions([]);

const fine = { guardHit: null, scope: usable, priceLoading: false, missingPrice: undefined };

describe('noAdviceReason', () => {
  it('沒有任何疑慮 → null，可以算建議', () => {
    expect(noAdviceReason(fine)).toBeNull();
  });

  it('分割疑慮最優先：沒有資產、其他原因都在時也一樣', () => {
    expect(
      noAdviceReason({ guardHit, scope: empty, priceLoading: true, missingPrice: '0050' }),
    ).toEqual({ kind: 'splitGuard', hit: guardHit });
  });

  it('沒有流動資產排在查價之前', () => {
    expect(noAdviceReason({ ...fine, scope: empty, priceLoading: true })).toEqual({
      kind: 'noScopeAssets',
    });
  });

  it('價格取得失敗與核心倍數異常一起回報', () => {
    expect(noAdviceReason({ ...fine, scope: unusable, priceLoading: true })).toEqual({
      kind: 'unusableHoldings',
      priceFailed: [failedPrice],
      coreFactorAnomaly: '00685L',
    });
  });

  it('查價中排在查不到價之前', () => {
    expect(noAdviceReason({ ...fine, priceLoading: true, missingPrice: '00631L' })).toEqual({
      kind: 'priceLoading',
    });
    expect(noAdviceReason({ ...fine, missingPrice: '00631L' })).toEqual({
      kind: 'priceMissing',
      symbol: '00631L',
    });
  });
});
