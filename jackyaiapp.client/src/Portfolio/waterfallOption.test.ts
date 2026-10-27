import { describe, expect, it } from 'vitest';

import { MASK } from './privacy';
import {
  buildWaterfallOption,
  formatAnchorValue,
  formatAxisTickValue,
  formatDeltaValue,
} from './waterfallOption';

describe('buildWaterfallOption y-axis range', () => {
  // Regression coverage for two bugs that both shipped to production before this test existed
  // (PR #266, then PR #267 fixing #266's own fix): a 0-anchored axis squashes small delta bars
  // into an unreadable sliver next to a much larger 投入成本/目前總值 total.
  const identity = (n: number) => String(n);

  it('does not force the axis floor to 0 when deltas cluster far above it', () => {
    // 投入成本 = 8,700,000; six small deltas (a few thousand each); 目前總值 similar magnitude.
    const items = [
      { name: 'A', value: 90_406 },
      { name: 'B', value: 40_000 },
      { name: 'C', value: 20_000 },
      { name: 'D', value: 10_000 },
      { name: 'E', value: 5_000 },
      { name: 'F', value: -246_862 },
    ];
    const startValue = 8_700_000;
    const option = buildWaterfallOption(items, startValue, identity, identity, identity);

    const yAxis = option.yAxis as { min: number; max: number };
    // The bug: yMin ended up ~0 (or worse, a large negative padding derived from the full
    // 0..startValue span) because the anchor bars' always-zero *base* was included in the range
    // calculation. The fix scopes the range to the anchors' *top* (the real value) plus the delta
    // bars' own edges — so yMin must sit close to the lowest point the bars actually occupy, far
    // above 0, not anywhere near it.
    expect(yAxis.min).toBeGreaterThan(startValue * 0.5);
    expect(yAxis.max).toBeGreaterThan(startValue);
  });

  it('excludes the anchor bars base (always 0) from the range even when it is the global min', () => {
    // Every value here is small and positive, so 0 (the anchor base) actually IS below
    // everything else — this pins that the fix doesn't need "0 happens not to be the min" to work.
    const items = [{ name: 'A', value: 10 }];
    const option = buildWaterfallOption(items, 100, identity, identity, identity);
    const yAxis = option.yAxis as { min: number; max: number };

    // Range should hug [100, 110] with padding, not stretch down toward 0.
    expect(yAxis.min).toBeGreaterThan(50);
  });

  it('handles an all-flat waterfall (identical start/end, no deltas) without a zero-width range', () => {
    const option = buildWaterfallOption([], 500_000, identity, identity, identity);
    const yAxis = option.yAxis as { min: number; max: number };
    expect(yAxis.max).toBeGreaterThan(yAxis.min);
  });
});

describe('buildWaterfallOption tooltip formatting', () => {
  it('routes the start/end anchor categories through formatAnchor, middle bars through formatDelta', () => {
    const items = [
      { name: 'Winner', value: 500 },
      { name: 'Loser', value: -300 },
    ];
    const option = buildWaterfallOption(
      items,
      1000,
      (n) => `ANCHOR(${n})`,
      (n) => `DELTA(${n})`,
      (n) => String(n),
    );
    const tooltip = option.tooltip as {
      formatter: (params: unknown) => string;
    };

    const anchorStart = tooltip.formatter([{ axisValue: '投入成本', dataIndex: 0 }]);
    expect(anchorStart).toContain('ANCHOR(1000)');

    const middleWinner = tooltip.formatter([{ axisValue: 'Winner', dataIndex: 1 }]);
    expect(middleWinner).toContain('DELTA(500)');
    expect(middleWinner).toContain('+'); // positive delta gets an explicit + sign

    const middleLoser = tooltip.formatter([{ axisValue: 'Loser', dataIndex: 2 }]);
    expect(middleLoser).toContain('DELTA(-300)');
    expect(middleLoser).not.toContain('+DELTA');

    const anchorEnd = tooltip.formatter([{ axisValue: '目前總值', dataIndex: 3 }]);
    expect(anchorEnd).toContain('ANCHOR(1200)'); // 1000 + 500 - 300
  });

  it('writes a holding name as text: ECharts puts the tooltip in with innerHTML (D-160)', () => {
    const option = buildWaterfallOption(
      [{ name: '<img src=x onerror=alert(1)>', value: 5 }],
      10,
      String,
      String,
      String,
    );
    const tooltip = option.tooltip as { formatter: (params: unknown) => string };

    const html = tooltip.formatter([{ axisValue: '<img src=x onerror=alert(1)>', dataIndex: 1 }]);
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
  });
});

describe('privacy-mode formatters', () => {
  // Regression coverage for PR #268: 'changes' mode ("只看損益") is specifically meant to show
  // P&L delta amounts while hiding absolute totals — an earlier version masked everything outside
  // 'full', hiding the whole chart in the one mode meant to show deltas.
  it('formatAnchorValue: shown only in full, masked in changes and hidden', () => {
    expect(formatAnchorValue('full', 1_000_000)).not.toBe(MASK);
    expect(formatAnchorValue('changes', 1_000_000)).toBe(MASK);
    expect(formatAnchorValue('hidden', 1_000_000)).toBe(MASK);
  });

  it('formatDeltaValue: shown in full AND changes, masked only in hidden', () => {
    expect(formatDeltaValue('full', 90_406)).not.toBe(MASK);
    expect(formatDeltaValue('changes', 90_406)).not.toBe(MASK);
    expect(formatDeltaValue('hidden', 90_406)).toBe(MASK);
  });

  it('formatAxisTickValue: only ever shown in full (ticks share the anchors scale)', () => {
    expect(formatAxisTickValue('full', 1_000_000)).not.toBe('');
    expect(formatAxisTickValue('changes', 1_000_000)).toBe('');
    expect(formatAxisTickValue('hidden', 1_000_000)).toBe('');
  });
});
