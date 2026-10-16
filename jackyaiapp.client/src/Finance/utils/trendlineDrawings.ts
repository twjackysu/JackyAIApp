/**
 * Trendlines the user can move, delete and draw on the K-line chart.
 *
 * The chart starts from the automatic lines of {@link analyzeTrendlines}. Once the user changes
 * anything, the whole set becomes theirs: it is kept in this browser's localStorage per code and
 * no longer follows the automatic lines, until they restore them.
 *
 * A line is anchored to dates, not bar indices, so it stays where it was drawn as new bars arrive
 * and an older bar drops off the left edge. Whether it is a support or a resistance is the user's
 * call (the automatic lines bring theirs): it stays put when the line is dragged across the price.
 */

import {
  DEFAULT_TRENDLINE_OPTIONS,
  lineAt,
  type OhlcBar,
  type TrendlineAnalysis,
  type TrendlineSide,
} from './trendlines';

import type { LineValue } from './trendlineChartOption';

import { readJson, removeKey, writeJson } from '@/utils/storage';

export interface LinePoint {
  /** `YYYY-MM-DD` of the bar the point sits on. */
  date: string;
  price: number;
}

export interface DrawnLine {
  id: string;
  from: LinePoint;
  to: LinePoint;
  /** Keep extending the line to the newest bar: it reached the last bar when it was drawn. */
  toLatest: boolean;
  /**
   * Support or resistance. Absent only on lines saved before it was stored; those are read off
   * the chart (most closes above the line = support) until they are next edited.
   */
  side?: TrendlineSide;
}

/** A line as it appears on the chart today. */
export interface LineGeometry {
  id: string;
  /** Bar index of the left end (fractional only if the line starts before the first bar). */
  x1: number;
  y1: number;
  /** Bar index of the right end. */
  x2: number;
  y2: number;
  side: TrendlineSide;
  /** Swing points on the line, counted as the automatic lines count them. */
  touches: number;
  /** Reaches the newest bar. */
  isActive: boolean;
}

export interface CurrentLevel {
  /** The line's value on the newest bar. */
  level: number;
  touches: number;
}

const DAY_MS = 86_400_000;
/** Bars per calendar day, to place a date that has dropped off the left edge of the chart. */
const TRADING_DAYS_PER_DAY = 5 / 7;

const daysBetween = (from: string, to: string) => (Date.parse(to) - Date.parse(from)) / DAY_MS;

/**
 * Bar index of `date`. A date between two bars maps to the later one; a date before the first bar
 * (or after the last) is extrapolated at five bars a week, so an old line keeps its slope.
 */
export function indexOfDate(dates: string[], date: string): number {
  let lo = 0;
  let hi = dates.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (dates[mid] < date) lo = mid + 1;
    else hi = mid;
  }
  if (lo === 0 && dates[0] !== date) return -daysBetween(date, dates[0]) * TRADING_DAYS_PER_DAY;
  if (lo === dates.length) {
    const last = dates.length - 1;
    return last + daysBetween(dates[last], date) * TRADING_DAYS_PER_DAY;
  }
  return lo;
}

/** Where `line` is drawn on these bars, or null if none of it falls on the chart. */
export function lineGeometry(
  bars: OhlcBar[],
  analysis: Pick<TrendlineAnalysis, 'atrs' | 'swings'>,
  line: DrawnLine,
  touchAtr = DEFAULT_TRENDLINE_OPTIONS.touchAtr,
): LineGeometry | null {
  const dates = bars.map((b) => b.date);
  const last = bars.length - 1;
  let a = indexOfDate(dates, line.from.date);
  let b = indexOfDate(dates, line.to.date);
  let pa = line.from.price;
  let pb = line.to.price;
  if (a === b || !Number.isFinite(pa) || !Number.isFinite(pb)) return null;
  if (a > b) [a, b, pa, pb] = [b, a, pb, pa];

  const slope = (pb - pa) / (b - a);
  const at = (k: number) => pa + slope * (k - a);
  const x1 = Math.max(a, 0);
  const x2 = line.toLatest ? last : Math.min(b, last);
  if (x2 <= x1) return null;

  let above = 0;
  let below = 0;
  for (let k = Math.ceil(x1); k <= x2; k++) {
    if (bars[k].close >= at(k)) above++;
    else below++;
  }
  const side: TrendlineSide = line.side ?? (above >= below ? 'support' : 'resistance');

  const touches = analysis.swings.filter(
    (s) =>
      s.isHigh === (side === 'resistance') &&
      s.index >= x1 &&
      s.index <= x2 &&
      Math.abs(s.price - at(s.index)) <= touchAtr * analysis.atrs[s.index],
  ).length;

  return { id: line.id, x1, y1: at(x1), x2, y2: at(x2), side, touches, isActive: x2 === last };
}

/** The automatic lines, as editable lines. */
export function autoDrawnLines(bars: OhlcBar[], analysis: TrendlineAnalysis): DrawnLine[] {
  return analysis.lines.map((l) => ({
    id: `auto-${l.side}-${l.start}-${l.end}`,
    from: { date: bars[l.start].date, price: lineAt(l, l.start) },
    to: { date: bars[l.end].date, price: lineAt(l, l.end) },
    toLatest: l.isActive,
    side: l.side,
  }));
}

/** For each side, the line reaching the newest bar closest to the last close. */
export function currentLevels(
  geometries: LineGeometry[],
  lastClose: number,
): Record<TrendlineSide, CurrentLevel | null> {
  const nearest = (side: TrendlineSide) => {
    const active = geometries
      .filter((g) => g.side === side && g.isActive)
      .sort((p, q) => Math.abs(p.y2 - lastClose) - Math.abs(q.y2 - lastClose));
    return active.length > 0 ? { level: active[0].y2, touches: active[0].touches } : null;
  };
  return { support: nearest('support'), resistance: nearest('resistance') };
}

/** Each line drawn over bar `index`, with its value there. */
export function lineValuesAt(geometries: LineGeometry[], index: number): LineValue[] {
  return geometries
    .filter((g) => g.x1 <= index && index <= g.x2)
    .map((g) => ({
      side: g.side,
      value: g.y1 + ((g.y2 - g.y1) / (g.x2 - g.x1)) * (index - g.x1),
    }));
}

// ── Edits ─────────────────────────────────────────────────────────────────────
//
// Every edit starts from the line as drawn (its geometry), snaps the ends to bars inside the
// chart, and never lets the two ends meet: a line through one bar has no slope.

const toBar = (bars: OhlcBar[], index: number) =>
  Math.min(bars.length - 1, Math.max(0, Math.round(index)));

function anchored(
  bars: OhlcBar[],
  id: string,
  side: TrendlineSide,
  x1: number,
  y1: number,
  x2: number,
  y2: number,
): DrawnLine {
  return {
    id,
    from: { date: bars[x1].date, price: y1 },
    to: { date: bars[x2].date, price: y2 },
    toLatest: x2 === bars.length - 1,
    side,
  };
}

/** The first whole bar of `line` and its value there: a line that begins off the chart is cut. */
function wholeStart(line: LineGeometry): { x1: number; y1: number } {
  const x1 = Math.ceil(line.x1);
  const slope = (line.y2 - line.y1) / (line.x2 - line.x1);
  return { x1, y1: line.y1 + slope * (x1 - line.x1) };
}

/** `line` with its left (`start`) or right (`end`) end moved to `index`, `price`. */
export function moveLineEnd(
  bars: OhlcBar[],
  line: LineGeometry,
  end: 'start' | 'end',
  index: number,
  price: number,
): DrawnLine {
  const { x1, y1 } = wholeStart(line);
  const x2 = Math.round(line.x2);
  return end === 'start'
    ? anchored(bars, line.id, line.side, Math.min(toBar(bars, index), x2 - 1), price, x2, line.y2)
    : anchored(bars, line.id, line.side, x1, y1, Math.max(toBar(bars, index), x1 + 1), price);
}

/** `line` shifted by `byBars` bars and `byPrice` in price, kept inside the chart, slope unchanged. */
export function moveLine(
  bars: OhlcBar[],
  line: LineGeometry,
  byBars: number,
  byPrice: number,
): DrawnLine {
  const { x1, y1 } = wholeStart(line);
  const x2 = Math.round(line.x2);
  const shift = Math.min(bars.length - 1 - x2, Math.max(-x1, Math.round(byBars)));
  return anchored(
    bars,
    line.id,
    line.side,
    x1 + shift,
    y1 + byPrice,
    x2 + shift,
    line.y2 + byPrice,
  );
}

/** A new `side` line between two points, or null if both fall on the same bar. */
export function createLine(
  bars: OhlcBar[],
  id: string,
  side: TrendlineSide,
  p: { index: number; price: number },
  q: { index: number; price: number },
): DrawnLine | null {
  const i = toBar(bars, p.index);
  const j = toBar(bars, q.index);
  if (i === j) return null;
  return i < j
    ? anchored(bars, id, side, i, p.price, j, q.price)
    : anchored(bars, id, side, j, q.price, i, p.price);
}

/** `line` as the other kind: a support becomes a resistance and vice versa, drawn where it was. */
export function withSide(bars: OhlcBar[], line: LineGeometry, side: TrendlineSide): DrawnLine {
  const { x1, y1 } = wholeStart(line);
  return anchored(bars, line.id, side, x1, y1, Math.round(line.x2), line.y2);
}

// ── Storage ───────────────────────────────────────────────────────────────────

/** A per-browser drawing, not account data — kept in localStorage like the Portfolio planners. */
const storageKey = (code: string) => `finance.trendlines.${code}`;

interface StoredDrawings {
  version: 1;
  lines: DrawnLine[];
}

const isPoint = (p: unknown): p is LinePoint =>
  typeof p === 'object' &&
  p !== null &&
  typeof (p as LinePoint).date === 'string' &&
  /^\d{4}-\d{2}-\d{2}$/.test((p as LinePoint).date) &&
  Number.isFinite((p as LinePoint).price);

const isLine = (l: unknown): l is DrawnLine =>
  typeof l === 'object' &&
  l !== null &&
  typeof (l as DrawnLine).id === 'string' &&
  isPoint((l as DrawnLine).from) &&
  isPoint((l as DrawnLine).to) &&
  typeof (l as DrawnLine).toLatest === 'boolean' &&
  [undefined, 'support', 'resistance'].includes((l as DrawnLine).side);

/** The user's lines for `code`, or null if they never changed the automatic ones. */
export function loadDrawings(code: string): DrawnLine[] | null {
  const stored = readJson<StoredDrawings>(storageKey(code));
  if (stored?.version !== 1 || !Array.isArray(stored.lines)) return null;
  // Drop what an older version or a hand edit left unreadable rather than the whole drawing.
  return stored.lines.filter(isLine);
}

export function saveDrawings(code: string, lines: DrawnLine[]): void {
  writeJson(storageKey(code), { version: 1, lines } satisfies StoredDrawings);
}

export function clearDrawings(code: string): void {
  removeKey(storageKey(code));
}
