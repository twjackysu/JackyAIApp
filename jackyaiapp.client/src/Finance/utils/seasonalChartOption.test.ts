import { describe, expect, it } from 'vitest';

import { computeSeasonal, type DateRange } from './seasonal';
import { buildSeasonalOption, formatSeasonalTooltip } from './seasonalChartOption';

import type { MarketIndexPoint } from '@/apis/financeApis/types';

const range = (sm: number, sd: number, em: number, ed: number): DateRange => ({
  start: { month: sm, day: sd },
  end: { month: em, day: ed },
});

const pts = (...rows: [string, number][]): MarketIndexPoint[] =>
  rows.map(([date, close]) => ({ date, close }));

// 2025: trades Thu 3/6 and Mon 3/10 (closed Fri–Sun). 2026 is the data year, current to 3/10.
const result = computeSeasonal(
  pts(
    ['2025-03-06', 20_000],
    ['2025-03-10', 21_000],
    ['2026-03-09', 50_000],
    ['2026-03-10', 51_000],
  ),
  '2026-03-10',
  range(3, 6, 3, 20),
);

const param = (year: number, key: string) => ({
  seriesName: String(year),
  marker: `<i>${year}</i>`,
  axisValue: key,
});

describe('formatSeasonalTooltip', () => {
  it('shows the real close and real date on a day the market was open', () => {
    const html = formatSeasonalTooltip(result, [param(2025, '03-10')]);

    expect(html).toContain('<b>3/10</b>');
    expect(html).toContain('21,000.00');
    expect(html).toContain('2025-03-10');
    expect(html).not.toContain('沿用');
  });

  it('repeats the previous close under its own date when the market was shut', () => {
    const html = formatSeasonalTooltip(result, [param(2025, '03-08')]); // Saturday

    expect(html).toContain('20,000.00');
    expect(html).toContain('休市沿用 2025-03-06');
    expect(html).not.toContain('2025-03-08');
  });

  it('reports "尚未開盤" before the year’s first trading day in the window', () => {
    // 2026 first trades 3/9, so 3/7 is before it.
    const html = formatSeasonalTooltip(result, [param(2026, '03-07')]);

    expect(html).toContain('尚未開盤');
    expect(html).not.toMatch(/\d{2},\d{3}\.\d{2}/);
  });

  it('reports "尚無資料" for the current year past its latest day instead of extending it', () => {
    const html = formatSeasonalTooltip(result, [param(2026, '03-15')]);

    expect(html).toContain('尚無資料');
    expect(html).not.toContain('51,000');
  });

  it('lists every year it is given, leaving out ones that are not drawn', () => {
    const html = formatSeasonalTooltip(result, [
      param(2025, '03-10'),
      param(2026, '03-10'),
      param(2019, '03-10'),
    ]);

    expect(html).toContain('2025');
    expect(html).toContain('2026');
    expect(html).not.toContain('2019');
  });

  it('is empty when nothing is hovered', () => {
    expect(formatSeasonalTooltip(result, [])).toBe('');
  });
});

describe('buildSeasonalOption', () => {
  const option = buildSeasonalOption(result, (line) => (line.isCurrent ? '#fff' : '#3B82F6'));

  it('has one named series per drawn year, bridging closed days and stopping at the data', () => {
    const series = option.series as {
      name: string;
      data: (number | null)[];
      connectNulls: boolean;
    }[];
    const axis = (option.xAxis as { data: string[] }).data;

    expect(series.map((s) => s.name)).toEqual(['2025', '2026']);
    expect(series.every((s) => s.connectNulls)).toBe(true);
    const current = series[1].data;
    expect(current[axis.indexOf('03-10')]).toBeCloseTo((51_000 / 50_000) * 100, 10);
    expect(current.slice(axis.indexOf('03-11')).every((v) => v === null)).toBe(true);
  });

  it('puts the years in the legend', () => {
    expect((option.legend as { data: string[] }).data).toEqual(['2025', '2026']);
  });

  it('formats x-axis labels as M/D', () => {
    const labelFormat = (option.xAxis as { axisLabel: { formatter: (v: string) => string } })
      .axisLabel.formatter;
    expect(labelFormat('03-06')).toBe('3/6');
  });
});
