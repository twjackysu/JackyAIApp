import { describe, expect, it } from 'vitest';

import { BENCHMARK_RETURN_TOOLTIP } from './benchmarkReturn';

describe('BENCHMARK_RETURN_TOOLTIP', () => {
  it('states the metric, that it includes dividends, and that it is not annualized', () => {
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('時間加權報酬率（TWR），含息、未年化');
  });

  it('explains why dividends are included, which is the whole point for a high-yield holder', () => {
    // Price-only return books a distribution as a loss and hands the lower-yielding benchmark an
    // unearned edge. If this rationale disappears the figure looks arbitrary again.
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('把配掉的錢當成虧損');
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('對照標的自己的配息同樣計入');
  });

  it('warns that the plotted line stays price-only, so it will not match the %', () => {
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('線仍是純市值（不含息）');
  });

  it('explains that contributions are excluded, which is the whole point of the fix', () => {
    // The reported bug was a +31.31% that was mostly deposits. If this line ever goes away the
    // number becomes unexplainable to the person reading it.
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('存進去的錢會被算成賺的');
  });

  it('answers why an ETF benchmark is comparable despite its own subscriptions', () => {
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('不動每股價格');
  });

  it('tells the reader why this differs from the XIRR shown on 總覽', () => {
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('XIRR');
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('資金加權');
  });
});
