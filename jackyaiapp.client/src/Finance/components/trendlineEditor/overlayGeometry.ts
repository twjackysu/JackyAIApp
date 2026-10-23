import type { ChartProjection } from './chartProjection';
import type { OhlcBar } from '../../utils/trendlines';
import type { PointerEvent as ReactPointerEvent } from 'react';

/** A point within this many pixels of a bar's high or low snaps to it. */
export const MAGNET_PX = 10;
/** A press that moves less than this is a click, not a drag. */
export const DRAG_SLOP_PX = 4;
export const HANDLE_RADIUS = 6;
/** Fingers need a bigger target than the dot they see. */
export const HANDLE_HIT_RADIUS = 18;
export const LINE_HIT_WIDTH = 18;
/** Pixels an arrow key moves a line up or down. */
const NUDGE_PX = 4;

export interface Point {
  index: number;
  price: number;
}

export interface Pixel {
  x: number;
  y: number;
}

export type Snap = (at: Pixel) => Point;

export const newLineId = () =>
  `user-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

/** Capturing keeps a drag going when the pointer leaves the element; jsdom has no capture. */
export function capture(e: ReactPointerEvent<Element>) {
  try {
    e.currentTarget.setPointerCapture(e.pointerId);
  } catch {
    /* not supported — the drag still works while the pointer stays on the chart */
  }
}

/** A pixel held inside the plot area. */
export function clampToPlot(plot: ChartProjection['plot'], at: Pixel): Pixel {
  return {
    x: Math.min(plot.right, Math.max(plot.left, at.x)),
    y: Math.min(plot.bottom, Math.max(plot.top, at.y)),
  };
}

/** The bar under a pixel, and its price — the bar's high or low when that is close. */
export function snapToBar(bars: OhlcBar[], projection: ChartProjection, at: Pixel): Point {
  const index = Math.min(bars.length - 1, Math.max(0, Math.round(projection.index(at.x))));
  const near = [bars[index].high, bars[index].low].find(
    (p) => Math.abs(projection.y(p) - at.y) <= MAGNET_PX,
  );
  return { index, price: near ?? projection.price(at.y) };
}

export type LineKeyAction = { kind: 'move'; byBars: number; byPrice: number } | { kind: 'delete' };

const ARROW_STEPS: Record<string, [number, number]> = {
  ArrowLeft: [-1, 0],
  ArrowRight: [1, 0],
  ArrowUp: [0, 1],
  ArrowDown: [0, -1],
};

/** What a key does to a focused line: an arrow moves it a bar or a few pixels, Delete removes it. */
export function lineKeyAction(key: string, projection: ChartProjection): LineKeyAction | null {
  if (key in ARROW_STEPS) {
    const [byBars, up] = ARROW_STEPS[key];
    const byPrice = up * Math.abs(projection.price(0) - projection.price(NUDGE_PX));
    return { kind: 'move', byBars, byPrice };
  }
  return key === 'Delete' || key === 'Backspace' ? { kind: 'delete' } : null;
}
