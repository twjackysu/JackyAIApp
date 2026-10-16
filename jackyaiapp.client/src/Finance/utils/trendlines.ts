/**
 * Automatic support/resistance trendlines, found the way a chartist draws them — no dates or
 * prices are hard-coded, only the relative thresholds in {@link TrendlineOptions}:
 *
 * 1. Swing points: a ZigZag whose reversal threshold is `swingAtr` × ATR(14), so a wiggle smaller
 *    than that (a fake top or bottom) never becomes a swing high or low.
 * 2. Candidates: every pair of swing lows (support) or swing highs (resistance) at most `maxSpan`
 *    bars apart. The line must hold between the two — at most a few closes beyond it. The newest
 *    swing is not confirmed yet, so it can touch a line but not define one.
 * 3. Life: from the first swing point until price breaks the line (two closes in a row 0.25 ATR
 *    beyond it, or one close a full ATR beyond), or until price has left it (`departAtr` ATRs away
 *    after its last touch). Lines may cross major waves.
 * 4. Score: touches × 3, minus closes beyond it × 2, minus its average distance from the closes in
 *    ATRs, plus a little for a longer life. A line under a fake bottom (closes keep crossing it) or
 *    a steep V rebound (price runs away from it) loses to one the market kept returning to.
 * 5. Selection: best first; a line overlapping an already chosen one on the same side by more than
 *    30% of the shorter is dropped.
 *
 * Tuned by eye on two years of the TAIEX; every threshold is a percentage or a multiple of ATR, so
 * the same settings apply to a stock at 30 and an index at 40,000.
 */

export interface OhlcBar {
  /** `YYYY-MM-DD` */
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
}

export interface TrendlineOptions {
  /** A swing point counts once price reverses this many ATRs from it. */
  swingAtr: number;
  /** Reversal that marks a major turn. */
  majorPct: number;
  /** A swing point within this many ATRs of a line touches it. */
  touchAtr: number;
  /** Most bars between the two swing points that define a line. */
  maxSpan: number;
  /** Fewest bars a line must live to be drawn. */
  minLife: number;
  /** Once price is this many ATRs from a line after its last touch, the line ends. */
  departAtr: number;
}

export const DEFAULT_TRENDLINE_OPTIONS: TrendlineOptions = {
  swingAtr: 2,
  majorPct: 0.09,
  touchAtr: 0.6,
  maxSpan: 150,
  minLife: 15,
  departAtr: 3,
};

export type TrendlineSide = 'support' | 'resistance';

export interface Swing {
  index: number;
  price: number;
  isHigh: boolean;
}

export interface Trendline {
  side: TrendlineSide;
  /** Bar index of the first swing point on the line. */
  start: number;
  /** Bar index where it was broken, left behind, or the last bar. */
  end: number;
  slope: number;
  intercept: number;
  /** Swing points on the line (at least 2). */
  touches: number;
  /** Still in force on the last bar. */
  isActive: boolean;
}

export interface TrendlineAnalysis {
  lines: Trendline[];
  swings: Swing[];
  majorSwings: Swing[];
  /** Value on the last bar of the support line still in force, if any. */
  support: number | null;
  /** Value on the last bar of the resistance line still in force, if any. */
  resistance: number | null;
}

export const lineAt = (line: Pick<Trendline, 'slope' | 'intercept'>, index: number) =>
  line.slope * index + line.intercept;

/** ATR(14) with Wilder smoothing; the first bar uses its own range. */
export function atr(bars: OhlcBar[], n = 14): number[] {
  const out: number[] = [];
  bars.forEach((b, i) => {
    const prev = bars[i - 1];
    const tr =
      i === 0
        ? b.high - b.low
        : Math.max(b.high - b.low, Math.abs(b.high - prev.close), Math.abs(b.low - prev.close));
    out.push(i === 0 ? tr : (out[i - 1] * (n - 1) + tr) / n);
  });
  return out;
}

/**
 * Alternating swing highs and lows. A high is confirmed only once price falls `threshold(bar,
 * high)` below it, and vice versa. The last point is the current extreme, not yet confirmed.
 */
export function zigzag(
  bars: OhlcBar[],
  threshold: (i: number, reference: number) => number,
): Swing[] {
  const swings: Swing[] = [];
  let up: boolean | null = null; // null until the first swing decides the direction
  let hi = 0;
  let lo = 0;
  const argMin = (from: number, to: number) => {
    let best = from;
    for (let k = from + 1; k <= to; k++) if (bars[k].low < bars[best].low) best = k;
    return best;
  };
  const argMax = (from: number, to: number) => {
    let best = from;
    for (let k = from + 1; k <= to; k++) if (bars[k].high > bars[best].high) best = k;
    return best;
  };

  for (let i = 1; i < bars.length; i++) {
    if (up !== false && bars[i].high >= bars[hi].high) hi = i;
    if (up !== true && bars[i].low <= bars[lo].low) lo = i;

    if (
      up !== false &&
      bars[hi].high - bars[i].low >= threshold(i, bars[hi].high) &&
      (up !== null || hi > lo)
    ) {
      swings.push({ index: hi, price: bars[hi].high, isHigh: true });
      up = false;
      lo = i > hi ? argMin(hi + 1, i) : i;
    } else if (
      up !== true &&
      bars[i].high - bars[lo].low >= threshold(i, bars[lo].low) &&
      (up !== null || lo > hi)
    ) {
      swings.push({ index: lo, price: bars[lo].low, isHigh: false });
      up = true;
      hi = i > lo ? argMax(lo + 1, i) : i;
    }
  }
  if (up === true) swings.push({ index: hi, price: bars[hi].high, isHigh: true });
  else if (up === false) swings.push({ index: lo, price: bars[lo].low, isHigh: false });
  return swings;
}

/** First bar from `from` where price breaks the line; `stop` if it never does. */
function breakIndex(
  bars: OhlcBar[],
  atrs: number[],
  support: boolean,
  slope: number,
  intercept: number,
  from: number,
  stop: number,
): number {
  const beyond = (k: number, m: number) => {
    const line = slope * k + intercept;
    return support ? bars[k].close < line - m * atrs[k] : bars[k].close > line + m * atrs[k];
  };
  for (let k = from; k < stop; k++)
    if (beyond(k, 1) || (k + 1 < stop && beyond(k, 0.25) && beyond(k + 1, 0.25))) return k;
  return stop;
}

function fitLines(
  bars: OhlcBar[],
  atrs: number[],
  swings: Swing[],
  support: boolean,
  opt: TrendlineOptions,
): Trendline[] {
  const cands = swings.filter((s) => s.isHigh === !support);
  // The last swing is only the current extreme, not yet confirmed by a reversal: it may count as a
  // touch, but a line through it would be "support" drawn through the bottom of a fall in progress.
  const provisional = swings[swings.length - 1];
  const n = bars.length;
  const scored: { score: number; line: Trendline }[] = [];

  for (let x = 0; x < cands.length; x++) {
    for (let y = x + 1; y < cands.length; y++) {
      if (cands[y] === provisional) continue;
      const { index: i, price: pi } = cands[x];
      const { index: j, price: pj } = cands[y];
      if (j - i < 5) continue;
      if (j - i > opt.maxSpan) break; // swings are in time order

      const slope = (pj - pi) / (j - i);
      const intercept = pi - slope * i;
      const at = (k: number) => slope * k + intercept;

      // The line has to hold between its two defining points.
      let violations = 0;
      for (let k = i; k <= j; k++)
        if (
          support ? bars[k].close < at(k) - 0.25 * atrs[k] : bars[k].close > at(k) + 0.25 * atrs[k]
        )
          violations++;
      if (violations > Math.max(1, 0.03 * (j - i + 1))) continue;

      const stop = breakIndex(bars, atrs, support, slope, intercept, j + 1, n);
      const touched = cands
        .filter(
          (c) =>
            c.index >= i &&
            c.index < stop &&
            Math.abs(c.price - at(c.index)) <= opt.touchAtr * atrs[c.index],
        )
        .map((c) => c.index);
      const lastTouch = Math.max(...touched);

      // Price left the line for good: stop drawing it there.
      let end = stop - 1;
      for (let k = lastTouch + 1; k < stop; k++) {
        if (Math.abs(bars[k].close - at(k)) > opt.departAtr * atrs[k]) {
          end = k;
          break;
        }
      }

      const life = end - i + 1;
      if (life < opt.minLife) continue;

      let gap = 0;
      for (let k = i; k <= end; k++) gap += Math.abs(bars[k].close - at(k)) / atrs[k];
      gap /= life;

      const score = touched.length * 3 - violations * 2 - gap + 0.02 * life;
      scored.push({
        score,
        line: {
          side: support ? 'support' : 'resistance',
          start: i,
          end,
          slope,
          intercept,
          touches: touched.length,
          isActive: end === n - 1,
        },
      });
    }
  }

  const chosen: Trendline[] = [];
  for (const { line } of scored.sort((a, b) => b.score - a.score)) {
    const overlaps = chosen.some(
      (c) =>
        Math.max(0, Math.min(line.end, c.end) - Math.max(line.start, c.start)) >
        0.3 * Math.min(line.end - line.start, c.end - c.start),
    );
    if (!overlaps) chosen.push(line);
  }
  return chosen;
}

export function analyzeTrendlines(
  bars: OhlcBar[],
  options: Partial<TrendlineOptions> = {},
): TrendlineAnalysis {
  const opt = { ...DEFAULT_TRENDLINE_OPTIONS, ...options };
  if (bars.length < opt.minLife * 2)
    return { lines: [], swings: [], majorSwings: [], support: null, resistance: null };

  const atrs = atr(bars);
  const swings = zigzag(bars, (i) => atrs[i] * opt.swingAtr);
  const majorSwings = zigzag(bars, (_, reference) => reference * opt.majorPct);
  const lines = [
    ...fitLines(bars, atrs, swings, true, opt),
    ...fitLines(bars, atrs, swings, false, opt),
  ].sort((a, b) => a.start - b.start);

  const last = bars.length - 1;
  const now = (side: TrendlineSide) => {
    const values = lines
      .filter((l) => l.side === side && l.isActive)
      .map((l) => lineAt(l, last))
      .sort((a, b) => Math.abs(a - bars[last].close) - Math.abs(b - bars[last].close));
    return values.length > 0 ? values[0] : null;
  };

  return { lines, swings, majorSwings, support: now('support'), resistance: now('resistance') };
}
