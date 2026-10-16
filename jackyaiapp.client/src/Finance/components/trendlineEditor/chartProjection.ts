import type { PriceRange } from '../../utils/trendlineChartOption';

/**
 * Where the candlestick chart put its axes, in pixels of the chart's own box. Two bars and the
 * fixed price range pin both axes down; both are linear, so that is all the overlay needs.
 */
export interface ChartFrame {
  /** Pixel x of the first bar's centre. */
  x0: number;
  /** Pixels from one bar's centre to the next. */
  xStep: number;
  bars: number;
  /** Pixel y of `min` and of `max`. */
  yMin: number;
  yMax: number;
  min: number;
  max: number;
}

export interface ChartProjection {
  x: (index: number) => number;
  y: (price: number) => number;
  index: (x: number) => number;
  price: (y: number) => number;
  /** The plot area, which the lines are clipped to. */
  plot: { left: number; top: number; right: number; bottom: number };
}

export function projectionOf(f: ChartFrame): ChartProjection {
  const perPrice = (f.yMax - f.yMin) / (f.max - f.min);
  return {
    x: (index) => f.x0 + index * f.xStep,
    index: (x) => (x - f.x0) / f.xStep,
    y: (price) => f.yMin + (price - f.min) * perPrice,
    price: (y) => f.min + (y - f.yMin) / perPrice,
    plot: {
      left: f.x0 - f.xStep / 2,
      right: f.x0 + (f.bars - 0.5) * f.xStep,
      top: Math.min(f.yMin, f.yMax),
      bottom: Math.max(f.yMin, f.yMax),
    },
  };
}

/** The part of the ECharts instance used here. */
export interface PixelConverter {
  convertToPixel: (
    finder: { xAxisIndex: number } | { yAxisIndex: number },
    value: unknown,
  ) => unknown;
}

/** Reads the frame off a laid-out chart, or null before ECharts has laid it out. */
export function measureFrame(
  chart: PixelConverter,
  dates: string[],
  range: PriceRange,
): ChartFrame | null {
  if (dates.length < 2) return null;
  let first, last, yMin, yMax;
  try {
    first = chart.convertToPixel({ xAxisIndex: 0 }, dates[0]);
    last = chart.convertToPixel({ xAxisIndex: 0 }, dates[dates.length - 1]);
    yMin = chart.convertToPixel({ yAxisIndex: 0 }, range.min);
    yMax = chart.convertToPixel({ yAxisIndex: 0 }, range.max);
  } catch {
    // An instance with no option yet (or already disposed) has no model to convert with; the
    // next layout measures again.
    return null;
  }
  const finite = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
  if (!finite(first) || !finite(last) || !finite(yMin) || !finite(yMax)) return null;
  if (first === last || yMin === yMax) return null;
  return {
    x0: first,
    xStep: (last - first) / (dates.length - 1),
    bars: dates.length,
    yMin,
    yMax,
    min: range.min,
    max: range.max,
  };
}

/** Frames within half a pixel draw the same; skipping those keeps re-renders down. */
export function sameFrame(a: ChartFrame | null, b: ChartFrame | null): boolean {
  if (a === null || b === null) return a === b;
  const near = (p: number, q: number) => Math.abs(p - q) < 0.5;
  return (
    a.bars === b.bars &&
    a.min === b.min &&
    a.max === b.max &&
    near(a.x0, b.x0) &&
    near(a.x0 + a.xStep * (a.bars - 1), b.x0 + b.xStep * (b.bars - 1)) &&
    near(a.yMin, b.yMin) &&
    near(a.yMax, b.yMax)
  );
}
