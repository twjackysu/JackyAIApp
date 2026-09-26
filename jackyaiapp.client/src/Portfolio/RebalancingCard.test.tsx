import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it } from 'vitest';

import { RebalancingCard } from './RebalancingCard';
import { saveRebalancingTarget } from './rebalancingTarget';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';

function makeSummary(overrides: Partial<PortfolioSummaryDto>): PortfolioSummaryDto {
  return {
    usdTwdRate: 32,
    totalNetWorthTWD: 0,
    twStockTWD: 0,
    usStockTWD: 0,
    cashTWD: 0,
    fundTWD: 0,
    realEstateTWD: 0,
    liabilityTWD: 0,
    liquidGrossAssetsTWD: 0,
    costBasisTWD: 0,
    unrealizedPnlTWD: 0,
    unrealizedPnlPct: 0,
    estimatedSellCostTWD: 0,
    totalDividendsTWD: 0,
    totalReturnPnlTWD: 0,
    totalReturnPct: 0,
    marketExposureTWD: 0,
    exposureOfLiquidAssetsPct: null,
    liquidExposureMultiple: null,
    drawdownBufferPct: null,
    positions: [],
    ...overrides,
  };
}

describe('RebalancingCard', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('first run (no saved target): seeds inputs with the current allocation, all deltas balanced', () => {
    const summary = makeSummary({
      twStockTWD: 500_000,
      cashTWD: 500_000,
      liquidGrossAssetsTWD: 1_000_000,
    });
    const html = renderToStaticMarkup(<RebalancingCard summary={summary} />);
    expect(html).toContain('台股');
    expect(html).toContain('美股');
    expect(html).toContain('基金');
    expect(html).toContain('現金');
    expect(html).toContain('不動產');
    // Target seeded from current allocation -> every bucket already balanced. (The intro
    // sentence itself mentions "買進"/"賣出" generically, so assert on the absence of an actual
    // money delta instead of those two words.)
    expect((html.match(/±持平/g) ?? []).length).toBe(5);
    expect(html).not.toContain('NT$');
  });

  it('a saved target that differs from the current allocation shows buy/sell deltas', () => {
    saveRebalancingTarget({ twStock: 80, usStock: 0, fund: 0, cash: 20, realEstate: 0 });
    const summary = makeSummary({
      twStockTWD: 200_000,
      cashTWD: 800_000,
      liquidGrossAssetsTWD: 1_000_000,
    });
    const html = renderToStaticMarkup(<RebalancingCard summary={summary} />);
    expect(html).toContain('買進'); // twStock: current 20% -> target 80%
    expect(html).toContain('賣出'); // cash: current 80% -> target 20%
  });

  it('excludes liability from the rebalancing buckets (exactly 5 target inputs, none for liability)', () => {
    const summary = makeSummary({
      cashTWD: 100_000,
      liquidGrossAssetsTWD: 100_000,
      liabilityTWD: 9_000_000,
    });
    const html = renderToStaticMarkup(<RebalancingCard summary={summary} />);
    expect((html.match(/type="number"/g) ?? []).length).toBe(5);
    expect(html).not.toContain('>負債<');
  });
});
