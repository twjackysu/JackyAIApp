// This file exports viz metadata (data with icon elements), not a component — Fast Refresh's
// component-only-export check doesn't apply to it.
/* eslint-disable react-refresh/only-export-components */
import AccountTreeIcon from '@mui/icons-material/AccountTree';
import BarChartIcon from '@mui/icons-material/BarChart';
import DonutLargeIcon from '@mui/icons-material/DonutLarge';
import DonutSmallIcon from '@mui/icons-material/DonutSmall';
import GridViewIcon from '@mui/icons-material/GridView';
import HubIcon from '@mui/icons-material/Hub';
import NumbersIcon from '@mui/icons-material/Numbers';
import PieChartIcon from '@mui/icons-material/PieChart';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import StackedBarChartIcon from '@mui/icons-material/StackedBarChart';
import TableChartIcon from '@mui/icons-material/TableChart';
import WaterfallChartIcon from '@mui/icons-material/WaterfallChart';

import type { VizCapability, VizId } from './types';

const INF = Number.POSITIVE_INFINITY;

// Each viz declares its own constraints; the matcher reads these so eligibility
// logic lives in exactly one place. gridDefault is in grid units (cols of 12, rows).
export const VIZ_CAPABILITIES: VizCapability[] = [
  {
    id: 'table',
    label: '表格 (Table)',
    icon: <TableChartIcon fontSize="small" />,
    minGroups: 0,
    maxGroups: INF,
    minMeasures: 0,
    maxMeasures: INF,
    gridDefault: { w: 12, h: 9 },
  },
  {
    id: 'kpi',
    label: 'KPI 大數字',
    icon: <NumbersIcon fontSize="small" />,
    minGroups: 0,
    maxGroups: INF,
    // Any number of measures — the card shows one at a time with a switcher.
    minMeasures: 1,
    maxMeasures: INF,
    gridDefault: { w: 4, h: 4 },
  },
  {
    id: 'column',
    label: '直條圖 (Column)',
    icon: <BarChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    gridDefault: { w: 8, h: 9 },
  },
  {
    id: 'bar',
    label: '橫條圖 (Bar)',
    icon: <BarChartIcon fontSize="small" sx={{ transform: 'rotate(90deg)' }} />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    gridDefault: { w: 8, h: 10 },
  },
  {
    id: 'line',
    label: '折線圖 (Line)',
    icon: <ShowChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    gridDefault: { w: 9, h: 8 },
  },
  {
    id: 'pie',
    label: '圓餅圖 (Pie)',
    icon: <PieChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 1,
    minMeasures: 1,
    maxMeasures: 1,
    gridDefault: { w: 5, h: 10 },
  },
  {
    id: 'donut',
    label: '甜甜圈圖 (Donut)',
    icon: <DonutLargeIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 1,
    minMeasures: 1,
    maxMeasures: 1,
    gridDefault: { w: 5, h: 10 },
  },
  {
    id: 'stackedColumn',
    label: '堆疊直條圖 (Stacked Column)',
    icon: <StackedBarChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    gridDefault: { w: 9, h: 9 },
  },
  {
    id: 'stackedBar',
    label: '堆疊橫條圖 (Stacked Bar)',
    icon: <StackedBarChartIcon fontSize="small" sx={{ transform: 'rotate(90deg)' }} />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    gridDefault: { w: 9, h: 10 },
  },
  {
    id: 'treemap',
    label: '樹狀圖 (Treemap)',
    icon: <GridViewIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: INF,
    minMeasures: 1,
    maxMeasures: 1,
    hierarchical: true,
    gridDefault: { w: 8, h: 10 },
  },
  {
    id: 'sunburst',
    label: '旭日圖 (Sunburst)',
    icon: <DonutSmallIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: INF,
    minMeasures: 1,
    maxMeasures: 1,
    hierarchical: true,
    gridDefault: { w: 6, h: 11 },
  },
  {
    id: 'tree',
    label: '樹狀階層圖 (Tree)',
    icon: <AccountTreeIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: INF,
    minMeasures: 1,
    maxMeasures: 1,
    hierarchical: true,
    gridDefault: { w: 10, h: 11 },
  },
  {
    id: 'waterfall',
    label: '瀑布圖 (Waterfall)',
    icon: <WaterfallChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 1,
    minMeasures: 1,
    maxMeasures: 1,
    requiresTemporal: true,
    minTemporalPoints: 2,
    gridDefault: { w: 10, h: 9 },
  },
  {
    id: 'sankey',
    label: '桑基圖 (Sankey)',
    icon: <HubIcon fontSize="small" />,
    minGroups: 2,
    maxGroups: INF,
    minMeasures: 1,
    maxMeasures: 1,
    hierarchical: true,
    gridDefault: { w: 10, h: 11 },
  },
];

const byId = new Map<VizId, VizCapability>(VIZ_CAPABILITIES.map((c) => [c.id, c]));

export function getCapability(id: VizId): VizCapability {
  const cap = byId.get(id);
  if (!cap) throw new Error(`Unknown viz id: ${id}`);
  return cap;
}
