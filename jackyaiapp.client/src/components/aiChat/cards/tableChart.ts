/**
 * 畫成圖 (D-153): a table card drawn as a chart, on the reader's device. Which columns can be drawn, what names a row,
 * which charts fit and the ECharts option are decided here, from the card alone: nothing goes to the server, no AI is
 * asked and nothing is charged. A tool's words stay data: the chart's text is drawn on a canvas, and the one place
 * ECharts writes HTML, the tooltip, gets them escaped.
 */
import { cellNumber } from './tableSort';

import type { CardCell, TableCard, TableColumn } from './toolCard';

export type TableChartKind = 'bar' | 'line' | 'pie';

export const TABLE_CHART_LABELS: Record<TableChartKind, string> = {
  bar: '長條圖',
  line: '折線圖',
  pie: '圓餅圖',
};

/** A bar per row up to this many, in the order on screen; more would not read in a side sheet. */
export const MAX_BARS = 20;
/** Slices up to this many; the rest are one 其他. */
export const MAX_SLICES = 8;

export interface TableChartPlan {
  /** The columns that can be drawn, one at a time (their scales differ: 市值 and 損益 %). */
  values: TableColumn[];
  /** What names a row: one column, or a code and the name after it (「2330 台積電」). */
  labelKeys: string[];
  /** Every row is named by a date or a period, so a line reads it in time order. */
  dated: boolean;
}

export interface TableChartData {
  labels: string[];
  values: number[];
  /** '%' when the column writes its numbers as percentages. */
  unit: string;
  /** Rows with a number that the chart leaves out (past `MAX_BARS`; folded into 其他 on a pie). */
  leftOut: number;
}

// A tool writes a missing number as a dash or N/A (停牌, no data yet): no point on the chart, not a word in the column.
const PLACEHOLDER = /^(|-{1,2}|—|–|N\/?A)$/i;
const isEmpty = (v: CardCell) =>
  v === null || (typeof v === 'string' && PLACEHOLDER.test(v.trim()));
const filledOf = (rows: TableCard['rows'], key: string) =>
  rows.map((r) => r[key] ?? null).filter((v) => !isEmpty(v));

// A code a stock or a ticket goes by: digits alone read as a number but name a row (2330, 0050).
const CODE = /^(?=.*\d)[A-Za-z0-9.-]{1,10}$/;
const DIGITS = /^\d+$/;

/**
 * A column whose cells are all numbers. One the tool did not mark numeric counts only when its text is written as a
 * number is (49.5%, 1,200, 3.5), so a column of codes (2330) or years stays a name.
 */
function isValueColumn(column: TableColumn, rows: TableCard['rows']): boolean {
  const filled = filledOf(rows, column.key);
  if (filled.length < 2 || !filled.every((v) => cellNumber(v) !== null)) return false;
  if (column.numeric) return !filled.every((v) => typeof v === 'string' && DIGITS.test(v.trim()));
  return filled.some((v) => typeof v === 'string' && /[.,%]/.test(v));
}

// 2026-10-27, 2026/10, 2026.10.27, 115/10/27 (民國), 2026Q3, 2026年10月, 20261027, 1150930 (民國, as TWSE writes it), 10/27.
const DATE =
  /^(\d{4}[-/.]\d{1,2}([-/.]\d{1,2})?|\d{2,3}\/\d{1,2}(\/\d{1,2})?|\d{4}\s?Q[1-4]|\d{4}年(\d{1,2}月(\d{1,2}日)?)?|(19|20)\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])|1\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])|\d{1,2}\/\d{1,2})$/;
// A month and a day with no year: they cannot be put in time order across a new year.
const YEARLESS = /^\d{1,2}\/\d{1,2}$/;

/** A date's parts as numbers, for time order: 2026Q3 → [2026, 3], 115/10/27 → [115, 10, 27]. */
const dateParts = (text: string) => (text.match(/\d+/g) ?? []).map(Number);

/** What a card can be drawn as, or null when it has no column of numbers, nothing to name the rows by, or one row. */
export function tableChartPlan(card: TableCard): TableChartPlan | null {
  if (card.rows.length < 2) return null;
  const values = card.columns.filter((c) => isValueColumn(c, card.rows));
  // A column the tool marked numeric never names a row, even one it cannot draw.
  const names = card.columns.filter((c) => !values.includes(c) && !c.numeric);
  if (values.length === 0 || names.length === 0) return null;
  const [first, second] = names;
  const firstCells = filledOf(card.rows, first.key);
  const coded =
    second !== undefined &&
    firstCells.every((v) => typeof v === 'string' && CODE.test(v.trim()) && !DATE.test(v.trim()));
  const labelKeys = coded ? [first.key, second.key] : [first.key];
  const dated =
    !coded &&
    firstCells.length === card.rows.length &&
    firstCells.every((v) => DATE.test(String(v).trim()));
  return { values, labelKeys, dated };
}

const labelOf = (row: Record<string, CardCell>, keys: readonly string[]) =>
  keys
    .map((k) => row[k] ?? null)
    .filter((v) => !isEmpty(v))
    .map((v) => String(v).trim())
    .join(' ') || '—';

/**
 * The column a chart starts with: the one the reader sorted by when it can be drawn (they sorted by what they care about),
 * else the first.
 */
export const firstValueKey = (plan: TableChartPlan, sortedKey: string | null): string =>
  plan.values.find((c) => c.key === sortedKey)?.key ?? plan.values[0].key;

/** The charts a column fits: a line only over time, a pie only for parts of a whole (no negatives, no percentages). */
export function tableChartKinds(
  card: TableCard,
  plan: TableChartPlan,
  key: string,
): TableChartKind[] {
  const numbers = card.rows.map((r) => cellNumber(r[key] ?? null)).filter((n) => n !== null);
  const kinds: TableChartKind[] = plan.dated ? ['line', 'bar'] : ['bar'];
  const label = card.columns.find((c) => c.key === key)?.label ?? '';
  const percent = unitOf(card.rows, key) === '%' || /%|率/.test(label);
  if (!percent && numbers.length >= 2 && numbers.every((n) => n >= 0) && numbers.some((n) => n > 0))
    kinds.push('pie');
  return kinds;
}

const unitOf = (rows: TableCard['rows'], key: string) => {
  const filled = filledOf(rows, key);
  return filled.length > 0 && filled.every((v) => typeof v === 'string' && v.trim().endsWith('%'))
    ? '%'
    : '';
};

/**
 * What the chart draws: the rows in the order on screen (so a sorted table is a ranked bar chart), a line in time order,
 * rows without a number in the column left out.
 */
export function tableChartData(
  rows: TableCard['rows'],
  plan: TableChartPlan,
  key: string,
  kind: TableChartKind,
): TableChartData {
  let points = rows
    .map((r) => ({ label: labelOf(r, plan.labelKeys), value: cellNumber(r[key] ?? null) }))
    .filter((p): p is { label: string; value: number } => p.value !== null);
  if (kind === 'line' && plan.dated) points = inTimeOrder(points);
  const unit = unitOf(rows, key);
  if (kind === 'bar' && points.length > MAX_BARS) {
    return { ...split(points.slice(0, MAX_BARS)), unit, leftOut: points.length - MAX_BARS };
  }
  if (kind === 'pie' && points.length > MAX_SLICES) {
    const kept = [...points].sort((a, b) => b.value - a.value);
    const rest = kept.slice(MAX_SLICES - 1);
    const other = { label: '其他', value: rest.reduce((sum, p) => sum + p.value, 0) };
    return { ...split([...kept.slice(0, MAX_SLICES - 1), other]), unit, leftOut: rest.length };
  }
  return { ...split(points), unit, leftOut: 0 };
}

const split = (points: { label: string; value: number }[]) => ({
  labels: points.map((p) => p.label),
  values: points.map((p) => p.value),
});

/**
 * Points by their dates, oldest first. Without a year (10/27) only the table's order says which year a date is in: kept,
 * or turned round when it mostly goes back in time (newest first, as tools list them; across a new year too).
 */
function inTimeOrder<T extends { label: string }>(points: T[]): T[] {
  if (!points.every((p) => YEARLESS.test(p.label))) {
    return [...points].sort((a, b) => compareDates(a.label, b.label));
  }
  let back = 0;
  for (let i = 1; i < points.length; i++) {
    if (compareDates(points[i - 1].label, points[i].label) > 0) back++;
  }
  return back > (points.length - 1) / 2 ? [...points].reverse() : points;
}

function compareDates(a: string, b: string): number {
  const pa = dateParts(a);
  const pb = dateParts(b);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (d !== 0) return d;
  }
  return 0;
}

/** The words under a chart that leaves rows out, or null. */
export function leftOutNotice(data: TableChartData, kind: TableChartKind): string | null {
  if (data.leftOut === 0) return null;
  return kind === 'pie'
    ? `較小的 ${data.leftOut} 筆合併成「其他」`
    : `只畫前 ${MAX_BARS} 筆（還有 ${data.leftOut} 筆，可以按欄名排序後再畫）`;
}

const escapeHtml = (text: string) =>
  text.replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] as string,
  );

export const formatChartValue = (value: number, unit: string) =>
  `${value.toLocaleString('zh-TW', { maximumFractionDigits: 2 })}${unit}`;

/** An axis or slice name cut to what fits beside a chart in a side sheet; the tooltip has it whole. */
export const shortLabel = (label: string, max = 10) => {
  const chars = [...label];
  return chars.length > max ? `${chars.slice(0, max - 1).join('')}…` : label;
};

export interface TableChartColors {
  text: string;
  line: string;
  accent: string;
  /** The tooltip's box and words: ECharts' own is white, wrong in dark mode. */
  surface: string;
  ink: string;
}

/** The chart's height: a bar per row, a fixed one for a line, a pie with room for its legend. */
export const tableChartHeight = (data: TableChartData, kind: TableChartKind) =>
  kind === 'bar'
    ? Math.max(120, 36 + data.labels.length * 26)
    : kind === 'pie'
      ? 240 + Math.ceil(data.labels.length / 3) * 20
      : 240;

/** The ECharts option for the chart (the shared registration draws bar, line and pie). */
export function tableChartOption(
  data: TableChartData,
  kind: TableChartKind,
  columnLabel: string,
  colors: TableChartColors,
): Record<string, unknown> {
  const tooltip = {
    trigger: 'item',
    confine: true,
    backgroundColor: colors.surface,
    borderColor: colors.line,
    textStyle: { color: colors.ink, fontSize: 12 },
    formatter: (p: { name: string; value: number; percent?: number }) =>
      `${escapeHtml(p.name)}<br/>${escapeHtml(columnLabel)}：${formatChartValue(p.value, data.unit)}` +
      (kind === 'pie' && p.percent !== undefined ? `（${p.percent}%）` : ''),
  };
  const axisText = { color: colors.text, fontSize: 11 };
  const value = (v: number) => formatChartValue(v, data.unit);
  if (kind === 'pie') {
    // Names in a legend under the ring, not on lines beside it: in a side sheet or on a phone there is no room beside it,
    // and ECharts cut them to「元…」. A slice big enough says its share inside.
    return {
      tooltip,
      legend: {
        bottom: 0,
        icon: 'circle',
        itemWidth: 8,
        itemHeight: 8,
        textStyle: { color: colors.text, fontSize: 11 },
        formatter: (name: string) => shortLabel(name, 12),
      },
      series: [
        {
          type: 'pie',
          center: ['50%', '42%'],
          radius: ['32%', '62%'],
          // A slice of nothing is no part of the whole, and its label would sit on its neighbour's.
          data: data.labels
            .map((name, i) => ({ name, value: data.values[i] }))
            .filter((d) => d.value > 0),
          label: {
            position: 'inside',
            color: '#fff',
            fontSize: 11,
            formatter: (p: { percent: number }) =>
              p.percent >= 8 ? `${Math.round(p.percent)}%` : '',
          },
          labelLine: { show: false },
        },
      ],
    };
  }
  const category = {
    type: 'category',
    data: data.labels,
    axisLabel: { ...axisText, formatter: (name: string) => shortLabel(name) },
    axisLine: { lineStyle: { color: colors.line } },
    axisTick: { show: false },
  };
  const valueAxis = {
    type: 'value',
    axisLabel: { ...axisText, formatter: value },
    splitLine: { lineStyle: { color: colors.line } },
  };
  if (kind === 'line') {
    return {
      tooltip: {
        ...tooltip,
        trigger: 'axis',
        formatter: (ps: { name: string; value: number }[]) =>
          ps
            .map((p) => `${escapeHtml(p.name)}<br/>${escapeHtml(columnLabel)}：${value(p.value)}`)
            .join('<br/>'),
      },
      grid: { left: 8, right: 16, top: 16, bottom: 8, containLabel: true },
      xAxis: category,
      yAxis: { ...valueAxis, scale: true },
      series: [
        {
          type: 'line',
          name: columnLabel,
          data: data.values,
          symbolSize: 5,
          itemStyle: { color: colors.accent },
          lineStyle: { color: colors.accent },
        },
      ],
    };
  }
  // A bar per row from the top, the first row on screen first: names read across, as in the table. The names stay at
  // the left edge (not at zero, among the bars going left), and a bar going left has its number on its left, inside the
  // plot: the axis reaches past the longest one by room for its number (the grid keeps room only for the names).
  return {
    tooltip,
    grid: { left: 8, right: 56, top: 4, bottom: 4, containLabel: true },
    yAxis: {
      ...category,
      inverse: true,
      axisLine: { onZero: false, lineStyle: { color: colors.line } },
    },
    xAxis: { ...valueAxis, axisLabel: { show: false }, min: barAxisMin },
    series: [
      {
        type: 'bar',
        data: data.values.map((v) => ({
          value: v,
          label: { position: v < 0 ? 'left' : 'right' },
        })),
        barMaxWidth: 16,
        itemStyle: { color: colors.accent, borderRadius: 3 },
        label: {
          show: true,
          color: colors.text,
          fontSize: 11,
          formatter: (p: { value: number }) => value(p.value),
        },
      },
    ],
  };
}

/** Where the bars' axis starts: at zero, or past the longest bar going left by room for its number. */
export function barAxisMin({ min, max }: { min: number; max: number }): number {
  if (min >= 0) return 0;
  return min - (Math.max(max, 0) - min) * 0.35;
}
