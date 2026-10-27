import { describe, expect, it } from 'vitest';

import {
  MAX_BARS,
  barAxisMin,
  MAX_SLICES,
  firstValueKey,
  leftOutNotice,
  shortLabel,
  tableChartData,
  tableChartKinds,
  tableChartOption,
  tableChartPlan,
} from './tableChart';

import type { TableCard } from './toolCard';

const card = (columns: TableCard['columns'], rows: TableCard['rows']): TableCard => ({
  kind: 'table',
  title: null,
  columns,
  rows,
  totalRows: null,
});

const holdings = card(
  [
    { key: 'name', label: '持倉', numeric: false },
    { key: 'value', label: '市值', numeric: true },
    { key: 'pct', label: '損益 %', numeric: true },
  ],
  [
    { name: '台積電（2330）', value: 1245000, pct: '49.5%' },
    { name: '長榮（2603）', value: 98000, pct: '-18.0%' },
    { name: '元大台灣50（0050）', value: 486000, pct: '14.4%' },
  ],
);

const colors = { text: '#888', line: '#ddd', accent: '#00f', surface: '#111', ink: '#eee' };

describe('tableChartPlan', () => {
  it('draws the number columns, one at a time, named by the first text column', () => {
    const plan = tableChartPlan(holdings)!;
    expect(plan.values.map((c) => c.key)).toEqual(['value', 'pct']);
    expect(plan.labelKeys).toEqual(['name']);
    expect(plan.dated).toBe(false);
  });

  it('names a row by its code and the name after it, and never draws a column of codes', () => {
    const foreign = card(
      [
        { key: 'code', label: '代號', numeric: false },
        { key: 'name', label: '名稱', numeric: false },
        { key: 'net', label: '買賣超', numeric: true },
      ],
      [
        { code: '2330', name: '台積電', net: 12345 },
        { code: '0050', name: '元大台灣50', net: -500 },
      ],
    );
    const plan = tableChartPlan(foreign)!;
    expect(plan.values.map((c) => c.key)).toEqual(['net']);
    expect(plan.labelKeys).toEqual(['code', 'name']);
    expect(tableChartData(foreign.rows, plan, 'net', 'bar').labels).toEqual([
      '2330 台積電',
      '0050 元大台灣50',
    ]);
  });

  it('counts a text column as numbers only when written as numbers are (1,200, 3.5%), not digits alone', () => {
    const plan = tableChartPlan(
      card(
        [
          { key: 'year', label: '年度', numeric: false },
          { key: 'shares', label: '股數', numeric: false },
        ],
        [
          { year: '2025', shares: '1,200' },
          { year: '2026', shares: '3,000' },
        ],
      ),
    )!;
    expect(plan.values.map((c) => c.key)).toEqual(['shares']);
    expect(plan.labelKeys).toEqual(['year']);
  });

  it('offers nothing without a number column, without names, or for one row', () => {
    const calendar = card(
      [
        { key: 'time', label: '時間', numeric: false },
        { key: 'title', label: '行程', numeric: false },
      ],
      [
        { time: '09:00–10:00', title: '週會' },
        { time: '14:00–15:00', title: '面試' },
      ],
    );
    expect(tableChartPlan(calendar)).toBeNull();
    expect(
      tableChartPlan(card([{ key: 'v', label: 'v', numeric: true }], [{ v: 1 }, { v: 2 }])),
    ).toBeNull();
    expect(tableChartPlan({ ...holdings, rows: holdings.rows.slice(0, 1) })).toBeNull();
  });

  it('leaves out a column with a word among its numbers', () => {
    const plan = tableChartPlan(
      card(
        [
          { key: 'name', label: '名稱', numeric: false },
          { key: 'v', label: '值', numeric: true },
        ],
        [
          { name: 'a', v: 1 },
          { name: 'b', v: '停牌' },
        ],
      ),
    );
    expect(plan).toBeNull();
  });
});

describe('a column over time', () => {
  const monthly = card(
    [
      { key: 'month', label: '月份', numeric: false },
      { key: 'revenue', label: '營收', numeric: true },
    ],
    [
      { month: '2026/09', revenue: 300 },
      { month: '2026/08', revenue: 200 },
      { month: '2026/10', revenue: 400 },
    ],
  );

  it('starts as a line in time order, whatever order the table is in', () => {
    const plan = tableChartPlan(monthly)!;
    expect(plan.dated).toBe(true);
    const kinds = tableChartKinds(monthly, plan, 'revenue');
    expect(kinds[0]).toBe('line');
    const data = tableChartData(monthly.rows, plan, 'revenue', 'line');
    expect(data.labels).toEqual(['2026/08', '2026/09', '2026/10']);
    expect(data.values).toEqual([200, 300, 400]);
  });

  it('keeps the order on screen as bars', () => {
    const plan = tableChartPlan(monthly)!;
    expect(tableChartData(monthly.rows, plan, 'revenue', 'bar').labels).toEqual([
      '2026/09',
      '2026/08',
      '2026/10',
    ]);
  });

  it.each(['2026-10-27', '115/10/27', '2026Q3', '2026年10月', '10/27'])(
    'reads %s as a date',
    (d) => {
      const plan = tableChartPlan(
        card(
          [
            { key: 'd', label: '日期', numeric: false },
            { key: 'v', label: '值', numeric: true },
          ],
          [
            { d, v: 1 },
            { d, v: 2 },
          ],
        ),
      )!;
      expect(plan.dated).toBe(true);
    },
  );
});

describe('tableChartKinds', () => {
  it('offers a pie for parts of a whole, never for percentages or negatives', () => {
    const plan = tableChartPlan(holdings)!;
    expect(tableChartKinds(holdings, plan, 'value')).toEqual(['bar', 'pie']);
    expect(tableChartKinds(holdings, plan, 'pct')).toEqual(['bar']);
    const withLoss = { ...holdings, rows: [...holdings.rows, { name: 'x', value: -1, pct: '1%' }] };
    expect(tableChartKinds(withLoss, tableChartPlan(withLoss)!, 'value')).toEqual(['bar']);
  });
});

describe('firstValueKey', () => {
  it('starts with the column the reader sorted by, when it can be drawn', () => {
    const plan = tableChartPlan(holdings)!;
    expect(firstValueKey(plan, 'pct')).toBe('pct');
    expect(firstValueKey(plan, 'name')).toBe('value');
    expect(firstValueKey(plan, null)).toBe('value');
  });
});

describe('tableChartData', () => {
  it('draws percentages as their numbers, says the unit, and skips a row without a number', () => {
    const rows = [...holdings.rows, { name: '新持倉', value: 1, pct: null }];
    const data = tableChartData(rows, tableChartPlan(holdings)!, 'pct', 'bar');
    expect(data.values).toEqual([49.5, -18, 14.4]);
    expect(data.unit).toBe('%');
    expect(data.labels).toHaveLength(3);
  });

  it(`draws the first ${MAX_BARS} bars in the order on screen and says how many it left out`, () => {
    const many = card(
      [
        { key: 'n', label: '名稱', numeric: false },
        { key: 'v', label: '值', numeric: true },
      ],
      Array.from({ length: 25 }, (_, i) => ({ n: `s${i}`, v: i })),
    );
    const data = tableChartData(many.rows, tableChartPlan(many)!, 'v', 'bar');
    expect(data.labels).toHaveLength(MAX_BARS);
    expect(data.labels[0]).toBe('s0');
    expect(data.leftOut).toBe(5);
    expect(leftOutNotice(data, 'bar')).toContain('還有 5 筆');
  });

  it(`folds the smallest slices past ${MAX_SLICES} into 其他`, () => {
    const many = card(
      [
        { key: 'n', label: '名稱', numeric: false },
        { key: 'v', label: '值', numeric: true },
      ],
      Array.from({ length: 10 }, (_, i) => ({ n: `s${i}`, v: i + 1 })),
    );
    const data = tableChartData(many.rows, tableChartPlan(many)!, 'v', 'pie');
    expect(data.labels).toHaveLength(MAX_SLICES);
    expect(data.labels[data.labels.length - 1]).toBe('其他');
    // 1 + 2 + 3 are the three smallest of ten.
    expect(data.values[data.values.length - 1]).toBe(6);
    expect(data.values.reduce((a, b) => a + b, 0)).toBe(55);
    expect(leftOutNotice(data, 'pie')).toBe('較小的 3 筆合併成「其他」');
  });
});

describe('tableChartOption', () => {
  const formatter = (option: Record<string, unknown>) =>
    (option.tooltip as { formatter: (p: unknown) => string }).formatter;

  it("escapes a tool's words in the tooltip, the one place ECharts writes HTML", () => {
    const data = { labels: ['<img src=x onerror=alert(1)>'], values: [1], unit: '', leftOut: 0 };
    const bar = formatter(tableChartOption(data, 'bar', '<b>值</b>', colors));
    const html = bar({ name: data.labels[0], value: 1 });
    expect(html).not.toContain('<img');
    expect(html).not.toContain('<b>');
    expect(html).toContain('&lt;img');
    const line = formatter(tableChartOption(data, 'line', '值', colors));
    expect(line([{ name: data.labels[0], value: 1 }])).not.toContain('<img');
  });

  it('keeps the names at the left edge and puts a bar going left its number on the left', () => {
    const data = { labels: ['a', 'b'], values: [3, -2], unit: '%', leftOut: 0 };
    const option = tableChartOption(data, 'bar', '損益 %', colors) as {
      yAxis: { inverse: boolean; axisLine: { onZero: boolean } };
      series: { data: { value: number; label: { position: string } }[] }[];
    };
    expect(option.yAxis.inverse).toBe(true);
    expect(option.yAxis.axisLine.onZero).toBe(false);
    expect(option.series[0].data.map((d) => d.label.position)).toEqual(['right', 'left']);
  });

  it('formats values with grouping and the unit', () => {
    const data = { labels: ['a'], values: [1245000], unit: '', leftOut: 0 };
    expect(
      formatter(tableChartOption(data, 'pie', '市值', colors))({
        name: 'a',
        value: 1245000,
        percent: 100,
      }),
    ).toBe('a<br/>市值：1,245,000（100%）');
  });
});

describe('shortLabel', () => {
  it('cuts by characters, never half an emoji', () => {
    expect(shortLabel('元大台灣50反1（00632R）')).toBe('元大台灣50反1（…');
    expect(shortLabel('😀'.repeat(12))).toBe(`${'😀'.repeat(9)}…`);
    expect(shortLabel('台積電')).toBe('台積電');
  });
});

describe('the review of D-153', () => {
  const twoCols = (rows: TableCard['rows'], numeric = true) =>
    card(
      [
        { key: 'd', label: '日期', numeric: false },
        { key: 'v', label: '值', numeric },
      ],
      rows,
    );

  it('counts a dash or N/A as no number, not as a word that spoils the column', () => {
    const t = twoCols([
      { d: '台積電', v: 1 },
      { d: '長榮', v: '—' },
      { d: '聯發科', v: 'N/A' },
      { d: '鴻海', v: 3 },
    ]);
    const plan = tableChartPlan(t)!;
    expect(plan.values.map((c) => c.key)).toEqual(['v']);
    expect(tableChartData(t.rows, plan, 'v', 'bar').labels).toEqual(['台積電', '鴻海']);
  });

  it('never names rows by a column the tool marked numeric', () => {
    const t = card(
      [
        { key: 'v', label: '值', numeric: true },
        { key: 'n', label: '名稱', numeric: false },
        { key: 'w', label: '量', numeric: true },
      ],
      [
        { v: 1, n: 'a', w: 2 },
        { v: '停牌', n: 'b', w: 3 },
      ],
    );
    expect(tableChartPlan(t)!.labelKeys).toEqual(['n']);
  });

  it('reads 20261027 and 1150930 as dates, not as codes', () => {
    for (const [a, b] of [
      ['20261028', '20261027'],
      ['1150930', '1150929'],
    ]) {
      const t = card(
        [
          { key: 'd', label: '日期', numeric: false },
          { key: 'n', label: '備註', numeric: false },
          { key: 'v', label: '值', numeric: true },
        ],
        [
          { d: a, n: 'x', v: 2 },
          { d: b, n: 'y', v: 1 },
        ],
      );
      const plan = tableChartPlan(t)!;
      expect(plan.dated).toBe(true);
      expect(plan.labelKeys).toEqual(['d']);
      expect(tableChartData(t.rows, plan, 'v', 'line').labels).toEqual([b, a]);
    }
  });

  it('puts month/day dates newest first back in time order, across a new year too', () => {
    const t = twoCols([
      { d: '01/05', v: 4 },
      { d: '01/02', v: 3 },
      { d: '12/30', v: 2 },
      { d: '12/29', v: 1 },
    ]);
    const data = tableChartData(t.rows, tableChartPlan(t)!, 'v', 'line');
    expect(data.labels).toEqual(['12/29', '12/30', '01/02', '01/05']);
    const forward = twoCols([...t.rows].reverse());
    expect(tableChartData(forward.rows, tableChartPlan(forward)!, 'v', 'line').labels).toEqual([
      '12/29',
      '12/30',
      '01/02',
      '01/05',
    ]);
  });

  it('takes a short English name for a name, not a code to join with the next column', () => {
    const t = card(
      [
        { key: 's', label: '代號', numeric: false },
        { key: 'sector', label: '產業', numeric: false },
        { key: 'v', label: '市值', numeric: true },
      ],
      [
        { s: 'AAPL', sector: 'Technology', v: 3 },
        { s: 'TSMC', sector: 'Semis', v: 1 },
      ],
    );
    expect(tableChartPlan(t)!.labelKeys).toEqual(['s']);
  });

  it('offers no pie for a column named as a rate, even in plain numbers', () => {
    const t = card(
      [
        { key: 'n', label: '名稱', numeric: false },
        { key: 'y', label: '殖利率', numeric: true },
      ],
      [
        { n: 'a', y: 3.2 },
        { n: 'b', y: 5.1 },
      ],
    );
    expect(tableChartKinds(t, tableChartPlan(t)!, 'y')).toEqual(['bar']);
  });

  it('leaves a slice of nothing out of the pie', () => {
    const data = { labels: ['a', 'b'], values: [3, 0], unit: '', leftOut: 0 };
    const option = tableChartOption(data, 'pie', '市值', colors) as {
      series: { data: { name: string }[] }[];
    };
    expect(option.series[0].data.map((d) => d.name)).toEqual(['a']);
  });

  it('starts the bars past the longest one going left, with room for its number', () => {
    expect(barAxisMin({ min: 0, max: 10 })).toBe(0);
    expect(barAxisMin({ min: 5, max: 10 })).toBe(0);
    // 49.5 to -18: 35% of the span past -18.
    expect(barAxisMin({ min: -18, max: 49.5 })).toBeCloseTo(-18 - 67.5 * 0.35);
    expect(barAxisMin({ min: -10, max: -2 })).toBeCloseTo(-13.5);
  });

  it('colours the tooltip for the theme, not ECharts white', () => {
    const tooltip = (
      tableChartOption(
        { labels: ['a'], values: [1], unit: '', leftOut: 0 },
        'bar',
        'v',
        colors,
      ) as {
        tooltip: { backgroundColor: string; textStyle: { color: string } };
      }
    ).tooltip;
    expect(tooltip.backgroundColor).toBe('#111');
    expect(tooltip.textStyle.color).toBe('#eee');
  });
});
