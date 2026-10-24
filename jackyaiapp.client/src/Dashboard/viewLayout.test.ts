import { describe, expect, it } from 'vitest';

import { STACK_BELOW_PX, viewLayout } from './viewLayout';

import type { DashboardWidget } from '@/apis/dashboardApis/types';

import { GRID_COLS } from '@/DataAnalysis/viz/ResizableWidget';

const widget = (id: string, x: number, y: number, w = 6, h = 8): DashboardWidget => ({
  id,
  title: id,
  vizType: 'line',
  plan: { viewBy: [], measures: [] },
  x,
  y,
  w,
  h,
});

// Saved side by side, then one below: right, left, below (out of reading order on purpose).
const saved = [widget('right', 6, 0), widget('left', 0, 0), widget('below', 0, 8, 12, 5)];

describe('viewLayout', () => {
  it('draws a wide screen as its owner arranged it', () => {
    expect(viewLayout(saved, 1200)).toEqual([
      { i: 'right', x: 6, y: 0, w: 6, h: 8, static: true },
      { i: 'left', x: 0, y: 0, w: 6, h: 8, static: true },
      { i: 'below', x: 0, y: 8, w: 12, h: 5, static: true },
    ]);
  });

  it('stacks a phone one chart a row, in reading order, each keeping its height', () => {
    expect(viewLayout(saved, STACK_BELOW_PX - 1)).toEqual([
      { i: 'left', x: 0, y: 0, w: GRID_COLS, h: 8, static: true },
      { i: 'right', x: 0, y: 8, w: GRID_COLS, h: 8, static: true },
      { i: 'below', x: 0, y: 16, w: GRID_COLS, h: 5, static: true },
    ]);
  });

  it('leaves the saved widgets as they were', () => {
    const before = JSON.stringify(saved);
    viewLayout(saved, 390);
    expect(JSON.stringify(saved)).toBe(before);
  });
});
