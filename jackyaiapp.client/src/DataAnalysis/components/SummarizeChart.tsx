import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import ReactEChartsCore from 'echarts-for-react/lib/core';
import * as echarts from 'echarts/core';
import { BarChart, LineChart, PieChart } from 'echarts/charts';
import {
  GridComponent,
  LegendComponent,
  TooltipComponent,
} from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import type { ComposeOption } from 'echarts/core';
import type { BarSeriesOption, LineSeriesOption, PieSeriesOption } from 'echarts/charts';
import type {
  GridComponentOption,
  LegendComponentOption,
  TooltipComponentOption,
} from 'echarts/components';

// ── ECharts registration (once at module load) ────────────────────────────────
echarts.use([
  BarChart, LineChart, PieChart,
  GridComponent, LegendComponent, TooltipComponent,
  CanvasRenderer,
]);

type ECOption = ComposeOption<
  | BarSeriesOption | LineSeriesOption | PieSeriesOption
  | GridComponentOption | LegendComponentOption | TooltipComponentOption
>;

// ── Public type ───────────────────────────────────────────────────────────────

export type ChartViewType =
  | 'table'
  | 'column'        // vertical bars
  | 'bar'           // horizontal bars
  | 'line'
  | 'pie'
  | 'stackedColumn'
  | 'stackedBar';

type ActiveChart = Exclude<ChartViewType, 'table'>;

// ── Colour palette ────────────────────────────────────────────────────────────

const PALETTE = [
  '#6366f1', '#22d3ee', '#f59e0b', '#10b981',
  '#f43f5e', '#8b5cf6', '#0ea5e9', '#ec4899',
  '#84cc16', '#f97316', '#06b6d4', '#a855f7',
];

// ── Constants ─────────────────────────────────────────────────────────────────

const CHART_HEIGHT      = 400;
const ANGLE_THRESHOLD   = 30;   // rotate X labels when > this many categories
const CLUTTER_THRESHOLD = 80;   // warn when > this many rows

// ── Column inference ──────────────────────────────────────────────────────────

function inferColumns(
  headers: string[],
  rows: Record<string, unknown>[],
): { groupKeys: string[]; measureKeys: string[] } {
  if (rows.length === 0) return { groupKeys: headers, measureKeys: [] };
  const sample = rows[0];
  const measureKeys: string[] = [];
  const groupKeys: string[] = [];
  headers.forEach((h) =>
    (typeof sample[h] === 'number' ? measureKeys : groupKeys).push(h),
  );
  return { groupKeys, measureKeys };
}

const trunc = (v: unknown, max = 20): string => {
  const s = String(v ?? '');
  return s.length > max ? `${s.slice(0, max - 1)}…` : s;
};

// ── Data preparation ──────────────────────────────────────────────────────────

interface CartesianData {
  /** X-axis (or Y-axis for horizontal) category labels */
  categories: string[];
  /** Each series is one line/bar group */
  series: Array<{ name: string; data: (number | null)[] }>;
  /** True when caller passed 3+ groupKeys; only first two were used */
  dimsTrimmed: boolean;
}

/**
 * Prepare data for all cartesian chart types (column, bar, line, stacked*).
 *
 * 1 groupKey  → categories = dim0 values,  series = one per measure
 * 2 groupKeys → categories = dim0 values,  series = dim1 × measure (pivot)
 * 3+ groupKeys → same as 2-groupKey using only the first two; dimsTrimmed = true
 */
function prepareCartesian(
  rows: Record<string, unknown>[],
  groupKeys: string[],
  measureKeys: string[],
): CartesianData {
  const dimsTrimmed = groupKeys.length > 2;
  const dims = groupKeys.slice(0, 2); // use at most 2 dimensions

  if (dims.length <= 1) {
    // ── Single dimension ────────────────────────────────────────────────────
    const dim0 = dims[0] ?? '';
    const categories = rows.map((r) => String(r[dim0] ?? ''));
    const series = measureKeys.map((mk) => ({
      name: mk,
      data: rows.map((r) => {
        const v = r[mk];
        return typeof v === 'number' ? v : parseFloat(String(v)) || 0;
      }),
    }));
    return { categories, series, dimsTrimmed };
  }

  // ── Two dimensions: pivot dim1 → series ──────────────────────────────────
  const [dim0, dim1] = dims;

  // Preserve first-seen order for both axes
  const xOrder = new Map<string, number>(); // dim0 value → index
  const sOrder = new Map<string, number>(); // dim1 value → index

  rows.forEach((r) => {
    const x = String(r[dim0] ?? '');
    const s = String(r[dim1] ?? '');
    if (!xOrder.has(x)) xOrder.set(x, xOrder.size);
    if (!sOrder.has(s)) sOrder.set(s, sOrder.size);
  });

  const categories  = [...xOrder.keys()];
  const dim1Values  = [...sOrder.keys()];

  // Build fast lookup: "xVal|||sVal" → { measureKey: number }
  const lookup = new Map<string, Record<string, number>>();
  rows.forEach((r) => {
    const key = `${String(r[dim0] ?? '')}|||${String(r[dim1] ?? '')}`;
    const nums: Record<string, number> = {};
    measureKeys.forEach((mk) => {
      const v = r[mk];
      nums[mk] = typeof v === 'number' ? v : parseFloat(String(v)) || 0;
    });
    lookup.set(key, nums);
  });

  const series: CartesianData['series'] = [];
  for (const sv of dim1Values) {
    for (const mk of measureKeys) {
      // If there is only one measure, keep series name short (just the dim1 value).
      // With multiple measures, append the measure name so the legend is unambiguous.
      const name =
        measureKeys.length === 1
          ? trunc(sv)
          : `${trunc(sv, 15)} · ${mk}`;
      const data = categories.map((xc) => {
        const entry = lookup.get(`${xc}|||${sv}`);
        return entry !== undefined ? entry[mk] : null;
      });
      series.push({ name, data });
    }
  }

  return { categories, series, dimsTrimmed };
}

interface PieData {
  slices: Array<{ name: string; value: number }>;
  /** True when groupKeys.length >= 2; pie only uses dim0 */
  multiDimWarning: boolean;
}

/**
 * Prepare data for pie chart.
 * Only the first groupKey is used as slices; dim1+ is ignored with a warning.
 * If multiple rows share the same dim0 value (due to dim1 being dropped), values are summed.
 */
function preparePie(
  rows: Record<string, unknown>[],
  groupKeys: string[],
  measureKeys: string[],
): PieData {
  const dim0    = groupKeys[0] ?? '';
  const measure = measureKeys[0];
  const multiDimWarning = groupKeys.length > 1;

  const agg = new Map<string, number>();
  rows.forEach((r) => {
    const name = trunc(String(r[dim0] ?? ''), 18);
    const v    = r[measure];
    const val  = typeof v === 'number' ? v : parseFloat(String(v)) || 0;
    agg.set(name, (agg.get(name) ?? 0) + val);
  });

  const slices = [...agg.entries()].map(([name, value]) => ({ name, value }));
  return { slices, multiDimWarning };
}

// ── Component ─────────────────────────────────────────────────────────────────

interface SummarizeChartProps {
  chartType: ActiveChart;
  rows: Record<string, unknown>[];
  headers: string[];
}

export function SummarizeChart({ chartType, rows, headers }: SummarizeChartProps) {
  const { groupKeys, measureKeys } = inferColumns(headers, rows);

  if (measureKeys.length === 0)
    return <Alert severity="info">No numeric measure columns — cannot render chart.</Alert>;
  if (rows.length === 0)
    return <Alert severity="info">No data to chart.</Alert>;

  // ── Pie: separate code path ───────────────────────────────────────────────
  if (chartType === 'pie') {
    const { slices, multiDimWarning } = preparePie(rows, groupKeys, measureKeys);
    const option: ECOption = {
      color:   PALETTE,
      tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
      legend:  { type: 'scroll', orient: 'horizontal', bottom: 0 },
      series: [{
        type:     'pie',
        radius:   '65%',
        center:   ['50%', '46%'],
        data:     slices,
        label:    { formatter: '{b}: {d}%' },
        emphasis: { itemStyle: { shadowBlur: 10, shadowColor: 'rgba(0,0,0,0.4)' } },
      } as PieSeriesOption],
    };

    return (
      <Box>
        {measureKeys.length > 1 && (
          <Alert severity="info" sx={{ mb: 1 }}>
            Pie shows the first measure only: <strong>{measureKeys[0]}</strong>.
          </Alert>
        )}
        {multiDimWarning && (
          <Alert severity="info" sx={{ mb: 1 }}>
            Pie uses only the first group-by (<strong>{groupKeys[0]}</strong>).
            Values are summed when multiple rows share the same category.
          </Alert>
        )}
        {rows.length > CLUTTER_THRESHOLD && (
          <Alert severity="warning" sx={{ mb: 1 }}>
            {slices.length} slices — pie charts are hard to read with many categories.
          </Alert>
        )}
        <Paper variant="outlined" sx={{ p: 1 }}>
          <ReactEChartsCore echarts={echarts} option={option}
            style={{ height: CHART_HEIGHT }} opts={{ renderer: 'canvas' }} notMerge />
        </Paper>
      </Box>
    );
  }

  // ── Cartesian charts (column / bar / line / stacked*) ─────────────────────
  const { categories, series, dimsTrimmed } = prepareCartesian(rows, groupKeys, measureKeys);
  const manyCategories = categories.length > ANGLE_THRESHOLD;

  const legend: LegendComponentOption = { type: 'scroll' };
  const gridBase: GridComponentOption = {
    containLabel: true,
    left: '3%', right: '4%',
    top: 40,
    bottom: manyCategories ? 80 : 40,
  };
  const gridHoriz: GridComponentOption = {
    containLabel: true,
    left: '3%', right: '4%',
    top: 40, bottom: 40,
  };

  const xAxisCategory = {
    type: 'category' as const,
    data: categories,
    axisLabel: {
      rotate:   manyCategories ? 35 : 0,
      interval: manyCategories ? ('auto' as const) : 0,
      overflow: 'truncate' as const,
      width: 80,
    },
  };
  const yAxisCategory = {
    type: 'category' as const,
    data: categories,
    axisLabel: { overflow: 'truncate' as const, width: 140 },
  };

  const mkBarSeries = (stacked: boolean): BarSeriesOption[] =>
    series.map((s) => ({
      ...s,
      type: 'bar' as const,
      ...(stacked ? { stack: 'total' } : {}),
    }));

  const mkLineSeries = (): LineSeriesOption[] =>
    series.map((s) => ({
      ...s,
      type: 'line' as const,
      showSymbol: rows.length <= 50,
      smooth: false,
    }));

  let option: ECOption;

  switch (chartType) {
    case 'column':
      option = {
        color: PALETTE, tooltip: { trigger: 'axis' }, legend,
        grid: gridBase,
        xAxis: xAxisCategory,
        yAxis: { type: 'value' },
        series: mkBarSeries(false),
      };
      break;

    case 'bar':
      option = {
        color: PALETTE, tooltip: { trigger: 'axis' }, legend,
        grid: gridHoriz,
        xAxis: { type: 'value' },
        yAxis: yAxisCategory,
        series: mkBarSeries(false),
      };
      break;

    case 'line':
      option = {
        color: PALETTE, tooltip: { trigger: 'axis' }, legend,
        grid: gridBase,
        xAxis: xAxisCategory,
        yAxis: { type: 'value' },
        series: mkLineSeries(),
      };
      break;

    case 'stackedColumn':
      option = {
        color: PALETTE,
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend,
        grid: gridBase,
        xAxis: xAxisCategory,
        yAxis: { type: 'value' },
        series: mkBarSeries(true),
      };
      break;

    case 'stackedBar':
      option = {
        color: PALETTE,
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend,
        grid: gridHoriz,
        xAxis: { type: 'value' },
        yAxis: yAxisCategory,
        series: mkBarSeries(true),
      };
      break;

    default:
      option = {};
  }

  return (
    <Box>
      {dimsTrimmed && (
        <Alert severity="warning" sx={{ mb: 1 }}>
          Chart view supports at most 2 group-by fields.
          Only <strong>{groupKeys[0]}</strong> (X-axis) and <strong>{groupKeys[1]}</strong> (series)
          are shown. Switch to table view to see all dimensions.
        </Alert>
      )}
      {groupKeys.length === 2 && (
        <Alert severity="info" sx={{ mb: 1 }}>
          X-axis: <strong>{groupKeys[0]}</strong> · Series: <strong>{groupKeys[1]}</strong>
        </Alert>
      )}
      {rows.length > CLUTTER_THRESHOLD && (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {rows.length} data points — consider adding filters or a coarser group-by
          for a cleaner chart.
        </Alert>
      )}
      <Paper variant="outlined" sx={{ p: 1 }}>
        <ReactEChartsCore
          echarts={echarts}
          option={option}
          style={{ height: CHART_HEIGHT }}
          opts={{ renderer: 'canvas' }}
          notMerge
        />
      </Paper>
    </Box>
  );
}
