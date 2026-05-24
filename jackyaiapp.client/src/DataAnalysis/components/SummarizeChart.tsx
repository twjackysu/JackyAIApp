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

// ── ECharts tree-shaking registration (runs once at module load) ───────────────
echarts.use([
  BarChart,
  LineChart,
  PieChart,
  GridComponent,
  LegendComponent,
  TooltipComponent,
  CanvasRenderer,
]);

// Compose a narrow option type so TypeScript catches invalid series options
type ECOption = ComposeOption<
  | BarSeriesOption
  | LineSeriesOption
  | PieSeriesOption
  | GridComponentOption
  | LegendComponentOption
  | TooltipComponentOption
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
];

// ── Constants ─────────────────────────────────────────────────────────────────

const CHART_HEIGHT = 400;
const ANGLE_THRESHOLD   = 30;  // rotate X labels when > this many points
const CLUTTER_THRESHOLD = 80;  // warn when > this many points

// ── Data helpers ──────────────────────────────────────────────────────────────

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

type ChartRow = { groupKey: string } & Record<string, number>;

function buildChartData(
  rows: Record<string, unknown>[],
  groupKeys: string[],
  measureKeys: string[],
): ChartRow[] {
  return rows.map((row) => {
    const groupKey =
      groupKeys.map((k) => String(row[k] ?? '')).join(' / ') || '(unknown)';
    const point: Record<string, string | number> = { groupKey };
    measureKeys.forEach((k) => {
      const v = row[k];
      point[k] = typeof v === 'number' ? v : parseFloat(String(v)) || 0;
    });
    return point as ChartRow;
  });
}

const trunc = (v: unknown, max = 20): string => {
  const s = String(v ?? '');
  return s.length > max ? `${s.slice(0, max - 1)}…` : s;
};

// ── Option builders ───────────────────────────────────────────────────────────

const legend: LegendComponentOption = { type: 'scroll' };

function cartesianXAxis(categoryData: string[], manyPoints: boolean) {
  return {
    type: 'category' as const,
    data: categoryData,
    axisLabel: {
      rotate: manyPoints ? 35 : 0,
      interval: manyPoints ? ('auto' as const) : 0,
      overflow: 'truncate' as const,
      width: 80,
    },
  };
}

function cartesianYAxis(categoryData: string[]) {
  return {
    type: 'category' as const,
    data: categoryData,
    axisLabel: { overflow: 'truncate' as const, width: 140 },
  };
}

// ── Component ─────────────────────────────────────────────────────────────────

interface SummarizeChartProps {
  chartType: ActiveChart;
  rows: Record<string, unknown>[];
  headers: string[];
}

export function SummarizeChart({ chartType, rows, headers }: SummarizeChartProps) {
  const { groupKeys, measureKeys } = inferColumns(headers, rows);
  const chartData = buildChartData(rows, groupKeys, measureKeys);

  if (measureKeys.length === 0)
    return <Alert severity="info">No numeric measure columns — cannot render chart.</Alert>;

  if (chartData.length === 0)
    return <Alert severity="info">No data to chart.</Alert>;

  const categoryData = chartData.map((d) => trunc(d.groupKey));
  const manyPoints   = chartData.length > ANGLE_THRESHOLD;

  // ── Bottom padding: more room when labels are angled ──
  const gridBase: GridComponentOption = {
    containLabel: true,
    left: '3%',
    right: '4%',
    top: 40,
    bottom: manyPoints ? 80 : 40,
  };
  const gridHoriz: GridComponentOption = {
    ...gridBase,
    left: '3%',   // containLabel handles the category labels
    bottom: 40,
  };

  let option: ECOption;

  switch (chartType) {
    // ── Vertical bars ─────────────────────────────────────────────────────────
    case 'column':
      option = {
        color: PALETTE,
        tooltip: { trigger: 'axis' },
        legend,
        grid: gridBase,
        xAxis: cartesianXAxis(categoryData, manyPoints),
        yAxis: { type: 'value' },
        series: measureKeys.map((k): BarSeriesOption => ({
          name: k, type: 'bar',
          data: chartData.map((r) => r[k]),
        })),
      };
      break;

    // ── Horizontal bars ───────────────────────────────────────────────────────
    case 'bar':
      option = {
        color: PALETTE,
        tooltip: { trigger: 'axis' },
        legend,
        grid: gridHoriz,
        xAxis: { type: 'value' },
        yAxis: cartesianYAxis(categoryData),
        series: measureKeys.map((k): BarSeriesOption => ({
          name: k, type: 'bar',
          data: chartData.map((r) => r[k]),
        })),
      };
      break;

    // ── Line ──────────────────────────────────────────────────────────────────
    case 'line':
      option = {
        color: PALETTE,
        tooltip: { trigger: 'axis' },
        legend,
        grid: gridBase,
        xAxis: cartesianXAxis(categoryData, manyPoints),
        yAxis: { type: 'value' },
        series: measureKeys.map((k): LineSeriesOption => ({
          name: k, type: 'line',
          data: chartData.map((r) => r[k]),
          showSymbol: chartData.length <= 50,
          smooth: false,
        })),
      };
      break;

    // ── Pie ───────────────────────────────────────────────────────────────────
    case 'pie': {
      const measure = measureKeys[0];
      option = {
        color: PALETTE,
        tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
        legend: { ...legend, orient: 'horizontal', bottom: 0 },
        series: [{
          type: 'pie',
          radius: '65%',
          center: ['50%', '48%'],
          data: chartData.map((r) => ({
            name: trunc(r.groupKey),
            value: r[measure],
          })),
          label:    { formatter: '{b}: {d}%' },
          emphasis: { itemStyle: { shadowBlur: 10, shadowColor: 'rgba(0,0,0,0.4)' } },
        } as PieSeriesOption],
      };
      break;
    }

    // ── Stacked vertical bars ─────────────────────────────────────────────────
    case 'stackedColumn':
      option = {
        color: PALETTE,
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend,
        grid: gridBase,
        xAxis: cartesianXAxis(categoryData, manyPoints),
        yAxis: { type: 'value' },
        series: measureKeys.map((k): BarSeriesOption => ({
          name: k, type: 'bar', stack: 'total',
          data: chartData.map((r) => r[k]),
        })),
      };
      break;

    // ── Stacked horizontal bars ───────────────────────────────────────────────
    case 'stackedBar':
      option = {
        color: PALETTE,
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend,
        grid: gridHoriz,
        xAxis: { type: 'value' },
        yAxis: cartesianYAxis(categoryData),
        series: measureKeys.map((k): BarSeriesOption => ({
          name: k, type: 'bar', stack: 'total',
          data: chartData.map((r) => r[k]),
        })),
      };
      break;

    default:
      option = {};
  }

  return (
    <Box>
      {chartType === 'pie' && measureKeys.length > 1 && (
        <Alert severity="info" sx={{ mb: 1 }}>
          Pie shows the first measure only: <strong>{measureKeys[0]}</strong>.
          Use Column / Stacked for multi-measure comparison.
        </Alert>
      )}
      {chartData.length > CLUTTER_THRESHOLD && (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {chartData.length} data points — consider adding filters or a coarser group-by
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
