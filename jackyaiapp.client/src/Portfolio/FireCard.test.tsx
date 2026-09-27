import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it } from 'vitest';

import { FireCard } from './FireCard';
import { saveFireTarget } from './fireTarget';

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

describe('FireCard', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('first run with no target set yet: prompts for a target instead of showing a false "already reached"', () => {
    const summary = makeSummary({ totalNetWorthTWD: 1_000_000, annualizedReturnPct: 8 });
    const html = renderToStaticMarkup(<FireCard summary={summary} />);
    expect(html).toContain('請先輸入目標淨值');
    expect(html).not.toContain('已達成目標');
  });

  it('seeds the growth-rate input from annualizedReturnPct when it is a real number, no placeholder caption', () => {
    const summary = makeSummary({ totalNetWorthTWD: 1_000_000, annualizedReturnPct: 8.5 });
    const html = renderToStaticMarkup(<FireCard summary={summary} />);
    expect(html).toMatch(/value="8\.5"/);
    expect(html).not.toContain('尚無足夠歷史資料');
  });

  it('seeds the growth-rate input from the placeholder and shows an explanatory caption when annualizedReturnPct is null', () => {
    const summary = makeSummary({ totalNetWorthTWD: 1_000_000, annualizedReturnPct: null });
    const html = renderToStaticMarkup(<FireCard summary={summary} />);
    expect(html).toMatch(/value="6"/);
    expect(html).toContain('尚無足夠歷史資料');
  });

  it('shows "already reached" when current net worth already meets a saved target', () => {
    saveFireTarget({
      targetNetWorthTWD: 500_000,
      annualGrowthRatePct: 5,
      monthlyContributionTWD: 0,
    });
    const summary = makeSummary({ totalNetWorthTWD: 1_000_000, annualizedReturnPct: 5 });
    const html = renderToStaticMarkup(<FireCard summary={summary} />);
    expect(html).toContain('已達成目標');
  });

  it('shows a projected year count for a reachable saved target', () => {
    saveFireTarget({
      targetNetWorthTWD: 2_000_000,
      annualGrowthRatePct: 10,
      monthlyContributionTWD: 0,
    });
    const summary = makeSummary({ totalNetWorthTWD: 1_000_000, annualizedReturnPct: 10 });
    const html = renderToStaticMarkup(<FireCard summary={summary} />);
    expect(html).toContain('預估');
    expect(html).toContain('年後達成目標');
  });

  it('shows the not-reachable message when the target is unreachable within 100 years', () => {
    saveFireTarget({
      targetNetWorthTWD: 1_000_000_000,
      annualGrowthRatePct: 0,
      monthlyContributionTWD: 0,
    });
    const summary = makeSummary({ totalNetWorthTWD: 1, annualizedReturnPct: 0 });
    const html = renderToStaticMarkup(<FireCard summary={summary} />);
    expect(html).toContain('此假設下無法在 100 年內達成');
  });
});
