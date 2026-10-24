import { describe, expect, it } from 'vitest';

import { CHART_LIMITS, parseChartCardData } from './chartCard';

const data = {
  viz: 'line',
  headers: ['month', 'revenue'],
  rows: [
    { month: '2026-01', revenue: 100, secret: 'dropped' },
    { month: '2026-02', revenue: '120' },
  ],
  groupHints: ['month'],
  temporalHints: ['month'],
  plan: { viewBy: [{ fieldName: 'OrderDate' }], measures: [{ fieldName: 'Amount' }] },
};

describe('parseChartCardData', () => {
  it('keeps the named columns of each row, and the plan', () => {
    expect(parseChartCardData(data)).toEqual({
      viz: 'line',
      headers: ['month', 'revenue'],
      rows: [
        { month: '2026-01', revenue: 100 },
        { month: '2026-02', revenue: '120' },
      ],
      groupHints: ['month'],
      temporalHints: ['month'],
      plan: data.plan,
    });
  });

  it('refuses a chart the page cannot draw', () => {
    expect(parseChartCardData({ ...data, viz: 'radar' })).toBeNull();
  });

  it('needs headers and rows', () => {
    expect(parseChartCardData({ ...data, headers: [] })).toBeNull();
    expect(parseChartCardData({ ...data, rows: 'x' })).toBeNull();
  });

  it('turns a cell that is not text or a number into an empty one', () => {
    const r = parseChartCardData({ ...data, rows: [{ month: { x: 1 }, revenue: Infinity }] });
    expect(r?.rows).toEqual([{ month: null, revenue: null }]);
  });

  it('keeps no plan that is not one, so the card cannot be added to a dashboard', () => {
    expect(parseChartCardData({ ...data, plan: { viewBy: 'x' } })?.plan).toBeNull();
  });

  it('caps what a tool can push into the answer', () => {
    const r = parseChartCardData({
      ...data,
      rows: Array.from({ length: 2000 }, () => ({ month: 'a', revenue: 1 })),
    });
    expect(r?.rows).toHaveLength(CHART_LIMITS.rows);
  });
});
