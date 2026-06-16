import BarChartIcon from '@mui/icons-material/BarChart';
import PieChartIcon from '@mui/icons-material/PieChart';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import StackedBarChartIcon from '@mui/icons-material/StackedBarChart';
import TableChartIcon from '@mui/icons-material/TableChart';

import type { VizCapability, VizId } from './types';

const INF = Number.POSITIVE_INFINITY;

// Phase 1 registry. Each viz declares its own constraints; the matcher reads
// these so eligibility logic lives in exactly one place.
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
];

const byId = new Map<VizId, VizCapability>(VIZ_CAPABILITIES.map((c) => [c.id, c]));

export function getCapability(id: VizId): VizCapability {
  const cap = byId.get(id);
  if (!cap) throw new Error(`Unknown viz id: ${id}`);
  return cap;
}
