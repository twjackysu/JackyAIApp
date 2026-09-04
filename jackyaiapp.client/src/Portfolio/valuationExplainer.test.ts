import { describe, expect, it } from 'vitest';

import { valuationExplainer } from './valuationExplainer';

import { PositionDto } from '@/apis/portfolioApis/types';

function position(overrides: Partial<PositionDto> = {}): PositionDto {
  return {
    assetType: 'STOCK',
    symbol: 'BRK-B',
    name: 'Berkshire Hathaway B',
    market: 'US',
    currency: 'USD',
    costCurrency: 'TWD',
    quantity: 2,
    currentPrice: 508.13,
    marketValueLocal: 1016.26,
    marketValueTWD: 32195,
    costBasisTWD: 32068,
    unrealizedPnlTWD: 127,
    unrealizedPnlPct: 0.4,
    exposureFactor: 1,
    fxRateUsed: 31.68,
    fxRateSource: 'CATHAY',
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
    ...overrides,
  } as PositionDto;
}

describe('valuationExplainer', () => {
  it('is absent for a TWD holding, which converts nothing', () => {
    expect(valuationExplainer(position({ fxRateUsed: null, fxRateSource: null }))).toBeNull();
  });

  it('states the three formulas a broker states', () => {
    const text = valuationExplainer(position())!;
    expect(text).toContain('市值 (TWD) = 股數 × 最新價 × 匯率');
    expect(text).toContain('未實現損益 = 市值 − 成本');
  });

  it('explains that a TWD cost is taken as entered, and why', () => {
    // The actionable part: the two apps only agree if the user enters the broker's own 總成本,
    // because the purchase-day 交割匯率 cannot be recovered afterwards.
    const text = valuationExplainer(position())!;
    expect(text).toContain('成本 (TWD) = 你輸入的金額，不再換匯');
    expect(text).toContain('買進當天的交割匯率');
    expect(text).toContain('已含買進手續費');
  });

  it('warns when the cost is held in a foreign currency instead', () => {
    const text = valuationExplainer(position({ costCurrency: 'USD' }))!;
    expect(text).toContain('成本 (TWD) = 原幣成本 × 同一個匯率');
    expect(text).toContain('愈差愈多');
  });

  it('names 國泰世華 and its matching formula for a US stock', () => {
    const text = valuationExplainer(position())!;
    expect(text).toContain('國泰世華銀行「最新價那一天」的即期買入匯率');
    expect(text).toContain('股數 × 最新價 × 昨收匯率');
  });

  it('names 集保 and uses fund wording for a fund', () => {
    const text = valuationExplainer(
      position({ assetType: 'FUND', fxRateSource: 'TDCC', symbol: '043183' }),
    )!;
    expect(text).toContain('市值 (TWD) = 單位數 × 淨值 × 匯率');
    expect(text).toContain('集保結算所與華南銀行議定的買入匯率');
  });

  it('says plainly that a mid-market rate will not match the broker', () => {
    const text = valuationExplainer(position({ fxRateSource: 'MID' }))!;
    expect(text).toContain('不是任何銀行的實際換匯價');
  });
});
