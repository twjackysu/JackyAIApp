import { describe, expect, it } from 'vitest';

import { BENCHMARK_RETURN_TOOLTIP } from './benchmarkReturn';

describe('BENCHMARK_RETURN_TOOLTIP', () => {
  it('states the metric and that it is not annualized', () => {
    expect(BENCHMARK_RETURN_TOOLTIP).toContain('時間加權報酬率（TWR），未年化');
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
