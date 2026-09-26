import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it } from 'vitest';

import { RebalancingCard } from './RebalancingCard';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';

function makeSummary(overrides: Partial<PortfolioSummaryDto> = {}): PortfolioSummaryDto {
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
  } as PortfolioSummaryDto;
}

describe('RebalancingCard', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('renders all 5 buckets and no liability row', () => {
    const html = renderToStaticMarkup(
      <RebalancingCard
        summary={makeSummary({
          twStockTWD: 400_000,
          usStockTWD: 200_000,
          fundTWD: 100_000,
          cashTWD: 300_000,
          realEstateTWD: 500_000,
          liquidGrossAssetsTWD: 1_000_000,
          liabilityTWD: 2_000_000,
        })}
      />,
    );

    expect(html).toContain('台股');
    expect(html).toContain('美股');
    expect(html).toContain('基金');
    expect(html).toContain('現金');
    expect(html).toContain('不動產');
    // The only "負債" in the markup is the footer caption's "不含負債" note — no liability
    // bucket row (with its own number input) is rendered.
    expect(html.match(/type="number"/g)).toHaveLength(5);
  });

  it('first run (no saved target) seeds targets from the current allocation, showing ~持平 deltas', () => {
    const html = renderToStaticMarkup(
      <RebalancingCard
        summary={makeSummary({
          twStockTWD: 500_000,
          cashTWD: 500_000,
          liquidGrossAssetsTWD: 1_000_000,
        })}
      />,
    );

    expect(html).toContain('±持平');
    expect(html).not.toContain('買入');
    expect(html).not.toContain('賣出');
  });

  it('shows a soft warning when the persisted target does not sum to ~100%', () => {
    localStorage.setItem(
      'portfolio.rebalancingTarget',
      JSON.stringify({ twStock: 10, usStock: 0, fund: 0, cash: 0, realEstate: 0 }),
    );

    const html = renderToStaticMarkup(
      <RebalancingCard
        summary={makeSummary({ cashTWD: 100_000, liquidGrossAssetsTWD: 100_000 })}
      />,
    );

    expect(html).toContain('未接近 100%');
  });

  it('renders buy/sell deltas once the persisted target diverges from current allocation', () => {
    localStorage.setItem(
      'portfolio.rebalancingTarget',
      JSON.stringify({ twStock: 0, usStock: 0, fund: 0, cash: 100, realEstate: 0 }),
    );

    const html = renderToStaticMarkup(
      <RebalancingCard
        summary={makeSummary({ twStockTWD: 100_000, cashTWD: 0, liquidGrossAssetsTWD: 100_000 })}
      />,
    );

    expect(html).toContain('買入');
    expect(html).toContain('賣出');
  });
});
