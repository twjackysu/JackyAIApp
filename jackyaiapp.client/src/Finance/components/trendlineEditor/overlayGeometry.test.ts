import { describe, expect, it } from 'vitest';

import { projectionOf } from './chartProjection';
import { clampToPlot, lineKeyAction, snapToBar } from './overlayGeometry';

import type { OhlcBar } from '../../utils/trendlines';

// Bar i is centred at x = 5 + 10i; price 80 is at y = 300 and 120 at y = 20 (7 px a point).
const projection = projectionOf({
  x0: 5,
  xStep: 10,
  bars: 3,
  yMin: 300,
  yMax: 20,
  min: 80,
  max: 120,
});
const bars: OhlcBar[] = [0, 1, 2].map((i) => ({
  date: `2026-01-0${i + 5}`,
  open: 100,
  high: 104,
  low: 96,
  close: 100,
}));

describe('clampToPlot', () => {
  it('keeps a pixel inside the plot', () => {
    expect(clampToPlot(projection.plot, { x: -50, y: 900 })).toEqual({ x: 0, y: 300 });
    expect(clampToPlot(projection.plot, { x: 900, y: -50 })).toEqual({ x: 30, y: 20 });
    expect(clampToPlot(projection.plot, { x: 12, y: 150 })).toEqual({ x: 12, y: 150 });
  });
});

describe('snapToBar', () => {
  it('rounds to the nearest bar, held to the bars there are', () => {
    expect(snapToBar(bars, projection, { x: 11, y: projection.y(110) }).index).toBe(1);
    expect(snapToBar(bars, projection, { x: 30, y: projection.y(110) }).index).toBe(2);
  });

  it('takes the bar’s high or low within 10 px, the price under the pointer beyond', () => {
    expect(snapToBar(bars, projection, { x: 5, y: projection.y(104) + 9 }).price).toBe(104);
    expect(snapToBar(bars, projection, { x: 5, y: projection.y(96) - 10 }).price).toBe(96);
    expect(snapToBar(bars, projection, { x: 5, y: projection.y(96) - 11 }).price).toBeCloseTo(
      96 + 11 / 7,
    );
  });
});

describe('lineKeyAction', () => {
  it('arrows move a bar sideways or 4 px up and down; Delete and Backspace remove; others nothing', () => {
    expect(lineKeyAction('ArrowLeft', projection)).toEqual({
      kind: 'move',
      byBars: -1,
      byPrice: 0,
    });
    expect(lineKeyAction('ArrowRight', projection)).toEqual({
      kind: 'move',
      byBars: 1,
      byPrice: 0,
    });
    expect(lineKeyAction('ArrowUp', projection)).toEqual({
      kind: 'move',
      byBars: 0,
      byPrice: expect.closeTo(4 / 7),
    });
    expect(lineKeyAction('ArrowDown', projection)).toEqual({
      kind: 'move',
      byBars: 0,
      byPrice: expect.closeTo(-4 / 7),
    });
    expect(lineKeyAction('Delete', projection)).toEqual({ kind: 'delete' });
    expect(lineKeyAction('Backspace', projection)).toEqual({ kind: 'delete' });
    expect(lineKeyAction('a', projection)).toBeNull();
  });
});
