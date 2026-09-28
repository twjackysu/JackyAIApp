import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { FireCard } from './FireCard';
import { loadFireTarget, saveFireTarget } from './fireTarget';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';

// React 19's `act` (imported from 'react' below) warns unless this is set — normally done by
// a testing-library setup file, which this project doesn't have. Same pattern as
// `Finance/hooks/useFinanceAnalysis.test.tsx`.
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** Sets a native input's value via its own value setter (bypassing React's tracked value) and
 * dispatches a real `input` event — the standard way to simulate a single keystroke without
 * `@testing-library/react` (not available in this project). */
function typeInto(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

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

  describe('typing a negative growth rate character-by-character', () => {
    let container: HTMLDivElement;
    let root: Root;

    beforeEach(() => {
      saveFireTarget({
        targetNetWorthTWD: 1_000_000,
        annualGrowthRatePct: 5,
        monthlyContributionTWD: 0,
      });
      container = document.createElement('div');
      document.body.appendChild(container);
      act(() => {
        root = createRoot(container);
        const summary = makeSummary({ totalNetWorthTWD: 500_000, annualizedReturnPct: 5 });
        root.render(<FireCard summary={summary} />);
      });
    });

    afterEach(() => {
      act(() => root.unmount());
      container.remove();
    });

    it('does not drop the sign — typing "-" then "5" ends up as -5, not 5', () => {
      // The growth-rate field is the second of the three TextFields.
      const input = container.querySelectorAll('input')[1] as HTMLInputElement;
      expect(input).toBeTruthy();

      act(() => typeInto(input, '-'));
      // Unlike `type="number"`, a `text` input does not sanitize an in-progress "-" to "" —
      // the field must still show exactly what was typed.
      expect(input.value).toBe('-');

      act(() => typeInto(input, '-5'));
      expect(input.value).toBe('-5');

      // Blur commits/resyncs the display; the value must stay -5, not flip to a positive number.
      // React implements onBlur via the (bubbling) native "focusout" event, not "blur".
      act(() => input.dispatchEvent(new FocusEvent('focusout', { bubbles: true })));
      expect(input.value).toBe('-5');
      expect(loadFireTarget()?.annualGrowthRatePct).toBe(-5);
    });
  });
});
