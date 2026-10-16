import { describe, expect, it } from 'vitest';

import taiex from './__fixtures__/taiex-2024-2026.json';
import { analyzeTrendlines, lineAt, zigzag, type OhlcBar } from './trendlines';

const taiexBars: OhlcBar[] = (taiex.bars as [string, number, number, number, number][]).map(
  ([date, open, high, low, close]) => ({ date, open, high, low, close }),
);

/** Bars from a close series: each opens at the previous close, with a small wick either side. */
function barsFrom(closes: number[]): OhlcBar[] {
  return closes.map((close, i) => {
    const open = i === 0 ? close : closes[i - 1];
    return {
      date: `d${i}`,
      open,
      high: Math.max(open, close) + 0.2,
      low: Math.min(open, close) - 0.2,
      close,
    };
  });
}

const describeLine = (bars: OhlcBar[], l: ReturnType<typeof analyzeTrendlines>['lines'][number]) =>
  `${l.side} ${bars[l.start].date} ${bars[l.end].date} ${l.touches}`;

describe('analyzeTrendlines on two years of the TAIEX', () => {
  // The same lines as the Python prototype the thresholds were tuned with (see the PR).
  it('finds the reviewed lines', () => {
    const result = analyzeTrendlines(taiexBars);

    expect(result.lines.map((l) => describeLine(taiexBars, l)).sort()).toEqual([
      'resistance 2024-11-08 2024-12-02 2',
      'resistance 2024-12-05 2025-09-08 6',
      'resistance 2025-08-19 2026-04-24 4',
      'resistance 2026-05-15 2026-06-08 2',
      'resistance 2026-06-03 2026-06-26 2',
      'resistance 2026-06-23 2026-07-17 2',
      'resistance 2026-07-17 2026-10-02 4',
      'support 2024-11-01 2025-02-18 5',
      'support 2025-02-03 2025-03-28 2',
      'support 2025-04-09 2025-05-16 2',
      'support 2025-06-02 2025-12-29 8',
      'support 2025-12-18 2026-02-10 2',
      'support 2026-02-02 2026-04-08 4',
      'support 2026-04-23 2026-07-15 5',
      'support 2026-08-25 2026-10-16 3',
    ]);
  });

  it('reports the support still in force today and no resistance once it was broken', () => {
    const result = analyzeTrendlines(taiexBars);

    expect(result.support).toBeCloseTo(47180.61, 1);
    expect(result.resistance).toBeNull();
    const active = result.lines.filter((l) => l.isActive);
    expect(active.map((l) => describeLine(taiexBars, l))).toEqual([
      'support 2026-08-25 2026-10-16 3',
    ]);
  });

  it('connects the 8/25 and 9/14 higher lows, not the 7/29 V-bottom', () => {
    const current = analyzeTrendlines(taiexBars).lines.find(
      (l) => l.side === 'support' && l.isActive,
    )!;
    const touchesOf = (date: string) => {
      const i = taiexBars.findIndex((b) => b.date === date);
      return Math.abs(taiexBars[i].low - lineAt(current, i));
    };

    expect(taiexBars[current.start].date).toBe('2026-08-25');
    expect(touchesOf('2026-09-14')).toBeLessThan(300);
  });
});

describe('zigzag', () => {
  it('ignores wiggles smaller than the threshold', () => {
    // A steady climb with ±1 wiggles: no reversal reaches 5, so only the end extreme is a swing.
    const closes = Array.from({ length: 60 }, (_, i) => 100 + i + (i % 2 === 0 ? 1 : -1));
    const swings = zigzag(barsFrom(closes), () => 5);

    expect(swings.filter((s) => s.index < 55)).toEqual([]);
  });

  it('confirms a high only after the reversal', () => {
    const closes = [100, 105, 110, 115, 120, 112, 104, 100, 98, 99];
    const swings = zigzag(barsFrom(closes), () => 10);

    // The top bar's high (120 + its 0.2 wick); the next bar opens there too, and a tie goes to the later bar.
    expect(swings[0]).toMatchObject({ price: 120.2, isHigh: true });
    expect(swings[1]).toMatchObject({ isHigh: false });
  });
});

describe('analyzeTrendlines on synthetic shapes', () => {
  it('draws a sideways range as flat support and resistance', () => {
    const closes = Array.from(
      { length: 160 },
      (_, i) => 105 + 5 * Math.sin((2 * Math.PI * i) / 20),
    );
    const bars = barsFrom(closes);
    const result = analyzeTrendlines(bars);

    const support = result.lines.find((l) => l.side === 'support')!;
    const resistance = result.lines.find((l) => l.side === 'resistance')!;
    expect(support.touches).toBeGreaterThanOrEqual(3);
    expect(resistance.touches).toBeGreaterThanOrEqual(3);
    expect(Math.abs(support.slope)).toBeLessThan(0.01);
    expect(Math.abs(resistance.slope)).toBeLessThan(0.01);
    expect(lineAt(support, 100)).toBeLessThan(lineAt(resistance, 100));
  });

  it('ends a rising support line before the crash that breaks it', () => {
    const rising = Array.from(
      { length: 100 },
      (_, i) => 100 + 0.3 * i + 3 * Math.sin((2 * Math.PI * i) / 16),
    );
    const crash = Array.from({ length: 20 }, (_, i) => rising[99] - 3 * (i + 1));
    const bars = barsFrom([...rising, ...crash]);
    const result = analyzeTrendlines(bars);

    const support = result.lines.find((l) => l.side === 'support' && l.start < 50)!;
    expect(support.slope).toBeGreaterThan(0.2);
    expect(support.isActive).toBe(false);
    // It lives through the channel. It may stop at the last rally top before the crash rather than
    // at the break: that rally runs more than departAtr ATRs above it, which counts as leaving it.
    expect(support.end).toBeGreaterThanOrEqual(90);
    expect(support.end).toBeLessThan(110);
    expect(result.support).toBeNull();
  });

  it('draws nothing on too little data', () => {
    const result = analyzeTrendlines(barsFrom([1, 2, 3, 2, 1, 2, 3, 2, 1, 2]));

    expect(result).toEqual({
      lines: [],
      atrs: [],
      swings: [],
      majorSwings: [],
      support: null,
      resistance: null,
    });
  });
});
