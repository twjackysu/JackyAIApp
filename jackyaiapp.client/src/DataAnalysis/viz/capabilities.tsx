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
// logic lives in exactly one place.
export const VIZ_CAPABILITIES: VizCapability[] = [
  {
    id: 'table',
    label: 'Table',
    icon: <TableChartIcon fontSize="small" />,
    minGroups: 0,
    maxGroups: INF,
    minMeasures: 0,
    maxMeasures: INF,
    defaultSize: { w: 760, h: 380 },
  },
  {
    id: 'kpi',
    label: 'KPI 大數字',
    icon: <NumbersIcon fontSize="small" />,
    minGroups: 0,
    maxGroups: 1,
    minMeasures: 1,
    maxMeasures: 1,
    // KPI shows a single number: only meaningful for a lone total, or a time
    // series (where it shows the latest value + YoY).
    customEligible: (shape) =>
      shape.groupCount === 0 || shape.temporalKey
        ? { ok: true }
        : { ok: false, reason: '需要單一總數，或一個時間維度（顯示最新值）' },
    defaultSize: { w: 300, h: 180 },
  },
  {
    id: 'column',
    label: 'Column',
    icon: <BarChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    defaultSize: { w: 640, h: 380 },
  },
  {
    id: 'bar',
    label: 'Bar (horiz.)',
    icon: <BarChartIcon fontSize="small" sx={{ transform: 'rotate(90deg)' }} />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    defaultSize: { w: 640, h: 420 },
  },
  {
    id: 'line',
    label: 'Line',
    icon: <ShowChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    defaultSize: { w: 680, h: 360 },
  },
  {
    id: 'pie',
    label: 'Pie',
    icon: <PieChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 1,
    minMeasures: 1,
    maxMeasures: 1,
    defaultSize: { w: 460, h: 420 },
  },
  {
    id: 'donut',
    label: 'Donut',
    icon: <DonutLargeIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 1,
    minMeasures: 1,
    maxMeasures: 1,
    defaultSize: { w: 460, h: 420 },
  },
  {
    id: 'stackedColumn',
    label: 'Stacked Column',
    icon: <StackedBarChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    defaultSize: { w: 680, h: 400 },
  },
  {
    id: 'stackedBar',
    label: 'Stacked Bar',
    icon: <StackedBarChartIcon fontSize="small" sx={{ transform: 'rotate(90deg)' }} />,
    minGroups: 1,
    maxGroups: 2,
    minMeasures: 1,
    maxMeasures: INF,
    defaultSize: { w: 680, h: 440 },
  },
  {
    id: 'treemap',
    label: 'Treemap',
    icon: <GridViewIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: INF,
    minMeasures: 1,
    maxMeasures: 1,
    hierarchical: true,
    defaultSize: { w: 560, h: 420 },
  },
  {
    id: 'sunburst',
    label: 'Sunburst',
    icon: <DonutSmallIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: INF,
    minMeasures: 1,
    maxMeasures: 1,
    hierarchical: true,
    defaultSize: { w: 480, h: 480 },
  },
  {
    id: 'tree',
    label: 'Tree (multi-level)',
    icon: <AccountTreeIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: INF,
    minMeasures: 1,
    maxMeasures: 1,
    hierarchical: true,
    defaultSize: { w: 700, h: 480 },
  },
  {
    id: 'waterfall',
    label: 'Waterfall',
    icon: <WaterfallChartIcon fontSize="small" />,
    minGroups: 1,
    maxGroups: 1,
    minMeasures: 1,
    maxMeasures: 1,
    requiresTemporal: true,
    minTemporalPoints: 2,
    defaultSize: { w: 700, h: 380 },
  },
  {
    id: 'sankey',
    label: 'Sankey',
    icon: <HubIcon fontSize="small" />,
    minGroups: 2,
    maxGroups: INF,
    minMeasures: 1,
    maxMeasures: 1,
    hierarchical: true,
    defaultSize: { w: 700, h: 480 },
  },
];

const byId = new Map<VizId, VizCapability>(VIZ_CAPABILITIES.map((c) => [c.id, c]));

export function getCapability(id: VizId): VizCapability {
  const cap = byId.get(id);
  if (!cap) throw new Error(`Unknown viz id: ${id}`);
  return cap;
}
