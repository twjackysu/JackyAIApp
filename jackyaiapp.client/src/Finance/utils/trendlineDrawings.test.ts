import { afterEach, describe, expect, it } from 'vitest';

import taiex from './__fixtures__/taiex-2024-2026.json';
import {
  autoDrawnLines,
  createLine,
  currentLevels,
  indexOfDate,
  lineGeometry,
  lineValuesAt,
  loadDrawings,
  moveLine,
  moveLineEnd,
  saveDrawings,
  withSide,
  type DrawnLine,
  type LineGeometry,
} from './trendlineDrawings';
import { analyzeTrendlines, lineAt, type OhlcBar } from './trendlines';

const taiexBars: OhlcBar[] = (taiex.bars as [string, number, number, number, number][]).map(
  ([date, open, high, low, close]) => ({ date, open, high, low, close }),
);

/** Weekday bars from 2026-01-05, each closing at `close(i)` with a range of ±1. */
function weekdayBars(count: number, close: (i: number) => number = () => 100): OhlcBar[] {
  const bars: OhlcBar[] = [];
  for (let t = Date.UTC(2026, 0, 5); bars.length < count; t += 86_400_000) {
    const day = new Date(t).getUTCDay();
    if (day === 0 || day === 6) continue;
    const c = close(bars.length);
    bars.push({
      date: new Date(t).toISOString().slice(0, 10),
      open: c,
      high: c + 1,
      low: c - 1,
      close: c,
    });
  }
  return bars;
}

const noSwings = { atrs: [], swings: [] };
const geometry = (bars: OhlcBar[], line: DrawnLine) =>
  lineGeometry(bars, noSwings, line) as LineGeometry;
const line = (from: [string, number], to: [string, number], toLatest = false): DrawnLine => ({
  id: 'l',
  from: { date: from[0], price: from[1] },
  to: { date: to[0], price: to[1] },
  toLatest,
});

describe('indexOfDate', () => {
  const dates = ['2026-01-05', '2026-01-06', '2026-01-08'];

  it('finds a bar, or the next one for a day without a bar', () => {
    expect(indexOfDate(dates, '2026-01-06')).toBe(1);
    expect(indexOfDate(dates, '2026-01-07')).toBe(2);
  });

  it('extrapolates five bars a week before the first bar and after the last', () => {
    expect(indexOfDate(dates, '2025-12-29')).toBeCloseTo(-5);
    expect(indexOfDate(dates, '2026-01-15')).toBeCloseTo(7);
  });
});

describe('the automatic lines as editable lines', () => {
  const analysis = analyzeTrendlines(taiexBars);
  const geometries = autoDrawnLines(taiexBars, analysis).map(
    (l) => lineGeometry(taiexBars, analysis, l) as LineGeometry,
  );

  it('draw exactly where the automatic lines are', () => {
    expect(geometries).toHaveLength(analysis.lines.length);
    geometries.forEach((g, n) => {
      const auto = analysis.lines[n];
      expect([g.x1, g.x2, g.side, g.touches, g.isActive]).toEqual([
        auto.start,
        auto.end,
        auto.side,
        auto.touches,
        auto.isActive,
      ]);
      expect(g.y1).toBeCloseTo(lineAt(auto, auto.start), 6);
      expect(g.y2).toBeCloseTo(lineAt(auto, auto.end), 6);
    });
  });

  it('give the same current support and resistance', () => {
    const levels = currentLevels(geometries, taiexBars[taiexBars.length - 1].close);
    expect(levels.support?.level).toBeCloseTo(analysis.support as number, 6);
    expect(levels.resistance).toBeNull();
    expect(analysis.resistance).toBeNull();
  });

  it('keep a line in force reaching the newest bar as days are added', () => {
    const active = autoDrawnLines(taiexBars, analysis).find((l) => l.toLatest) as DrawnLine;
    const before = lineGeometry(taiexBars, analysis, active) as LineGeometry;
    const later = [
      ...taiexBars,
      ...weekdayBars(5).map((b, i) => ({ ...b, date: `2026-10-${20 + i}` })),
    ];
    const after = geometry(later, active);
    const slope = (before.y2 - before.y1) / (before.x2 - before.x1);
    expect(after.x2).toBe(later.length - 1);
    expect(after.y2).toBeCloseTo(before.y2 + slope * 5, 6);
  });
});

describe('lineGeometry', () => {
  const bars = weekdayBars(30);

  it('keeps the kind of line it was given, wherever it is drawn', () => {
    const above = line(['2026-01-05', 105], ['2026-01-20', 105]);
    expect(geometry(bars, { ...above, side: 'support' }).side).toBe('support');
    expect(geometry(bars, { ...above, side: 'resistance' }).side).toBe('resistance');
  });

  it('reads the kind off the chart for a line saved without one: support when most closes are above', () => {
    expect(geometry(bars, line(['2026-01-05', 95], ['2026-01-20', 95])).side).toBe('support');
    expect(geometry(bars, line(['2026-01-05', 105], ['2026-01-20', 105])).side).toBe('resistance');
  });

  it('cuts a line that began before the first bar at the left edge, keeping its slope', () => {
    const g = geometry(bars, line(['2025-12-29', 90], ['2026-01-12', 100]));
    expect(g.x1).toBe(0);
    expect(g.y1).toBeCloseTo(95);
  });

  it('has nothing to draw for a line wholly before the chart, or one with both ends on a bar', () => {
    expect(lineGeometry(bars, noSwings, line(['2025-11-03', 90], ['2025-12-01', 95]))).toBeNull();
    expect(lineGeometry(bars, noSwings, line(['2026-01-06', 90], ['2026-01-06', 95]))).toBeNull();
  });

  it('counts the swing points on the line as touches', () => {
    const atrs = bars.map(() => 2);
    const swings = [
      { index: 2, price: 95.5, isHigh: false },
      { index: 9, price: 94.8, isHigh: false },
      { index: 15, price: 99, isHigh: false }, // too far above the line
      { index: 12, price: 105, isHigh: true }, // a high is no touch of a support line
    ];
    const g = lineGeometry(bars, { atrs, swings }, line(['2026-01-05', 95], ['2026-01-30', 95]));
    expect(g?.touches).toBe(2);
  });
});

describe('lineValuesAt', () => {
  const bars = weekdayBars(30);
  const geometries = [
    geometry(bars, { ...line([bars[5].date, 90], [bars[15].date, 95]), side: 'support' }),
    geometry(bars, { ...line([bars[10].date, 110], [bars[20].date, 105]), side: 'resistance' }),
  ];

  it('gives each line over a bar with its value there', () => {
    expect(lineValuesAt(geometries, 3)).toEqual([]);
    expect(lineValuesAt(geometries, 7)).toEqual([{ side: 'support', value: 91 }]);
    expect(lineValuesAt(geometries, 12)).toEqual([
      { side: 'support', value: 93.5 },
      { side: 'resistance', value: 109 },
    ]);
  });
});

describe('editing', () => {
  const bars = weekdayBars(30);
  const last = bars.length - 1;
  const base = geometry(bars, {
    ...line([bars[5].date, 90], [bars[15].date, 95]),
    side: 'resistance',
  });

  it('keeps the kind of line through every edit', () => {
    expect(moveLineEnd(bars, base, 'end', 20, 120).side).toBe('resistance');
    expect(moveLine(bars, base, 0, -30).side).toBe('resistance');
  });

  it('turns a line into the other kind where it stands', () => {
    const turned = withSide(bars, base, 'support');
    expect(turned).toEqual({
      id: 'l',
      from: { date: bars[5].date, price: 90 },
      to: { date: bars[15].date, price: 95 },
      toLatest: false,
      side: 'support',
    });
  });

  it('moves one end, snapped to a bar inside the chart', () => {
    const moved = moveLineEnd(bars, base, 'end', 20.4, 97);
    expect(moved.from).toEqual({ date: bars[5].date, price: 90 });
    expect(moved.to).toEqual({ date: bars[20].date, price: 97 });
    expect(moved.toLatest).toBe(false);
    expect(moveLineEnd(bars, base, 'end', 99, 97).to.date).toBe(bars[last].date);
    expect(moveLineEnd(bars, base, 'end', 99, 97).toLatest).toBe(true);
  });

  it('never lets one end pass the other', () => {
    expect(moveLineEnd(bars, base, 'start', 25, 90).from.date).toBe(bars[14].date);
    expect(moveLineEnd(bars, base, 'end', 2, 90).to.date).toBe(bars[6].date);
  });

  it('moves the whole line without changing its slope, stopping at the edges', () => {
    const moved = moveLine(bars, base, 3, 2);
    expect(moved.from).toEqual({ date: bars[8].date, price: 92 });
    expect(moved.to).toEqual({ date: bars[18].date, price: 97 });
    const pushed = moveLine(bars, base, 100, 0);
    expect(pushed.to.date).toBe(bars[last].date);
    expect(pushed.from.date).toBe(bars[last - 10].date);
    expect(pushed.toLatest).toBe(true);
    expect(moveLine(bars, base, -100, 0).from.date).toBe(bars[0].date);
  });

  it('creates a line of the chosen kind left to right, and none with both ends on one bar', () => {
    const created = createLine(
      bars,
      'n',
      'resistance',
      { index: 20, price: 99 },
      { index: 3.2, price: 91 },
    );
    expect(created).toEqual({
      id: 'n',
      from: { date: bars[3].date, price: 91 },
      to: { date: bars[20].date, price: 99 },
      toLatest: false,
      side: 'resistance',
    });
    expect(
      createLine(bars, 'n', 'support', { index: 4, price: 91 }, { index: 4.3, price: 99 }),
    ).toBeNull();
  });
});

describe('storage', () => {
  afterEach(() => localStorage.clear());
  const saved = line(['2026-01-05', 90], ['2026-01-20', 95], true);

  it('keeps the drawing per code', () => {
    expect(loadDrawings('2330')).toBeNull();
    saveDrawings('2330', [saved]);
    expect(loadDrawings('2330')).toEqual([saved]);
    expect(loadDrawings('2317')).toBeNull();
  });

  it('ignores what it cannot read', () => {
    localStorage.setItem('finance.trendlines.A', '{not json');
    localStorage.setItem('finance.trendlines.B', JSON.stringify({ version: 2, lines: [saved] }));
    localStorage.setItem(
      'finance.trendlines.C',
      JSON.stringify({
        version: 1,
        lines: [
          saved,
          { id: 'x', from: { date: 'soon', price: 1 } },
          { ...saved, id: 'y', side: 'sideways' },
        ],
      }),
    );
    expect(loadDrawings('A')).toBeNull();
    expect(loadDrawings('B')).toBeNull();
    expect(loadDrawings('C')).toEqual([saved]);
  });
});
