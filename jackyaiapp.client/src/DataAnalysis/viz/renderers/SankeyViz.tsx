import Alert from '@mui/material/Alert';

import { PALETTE, type ECOption } from '../../components/summarizeChart/echarts';
import { buildSankey } from '../dataPrep';

import { EChart } from './EChart';

// Registers sankey with ECharts; without it the chart renders as an empty canvas.
import '../registerDataAnalysisCharts';

interface SankeyVizProps {
  rows: Record<string, unknown>[];
  groupKeys: string[];
  measureKeys: string[];
}

/** Strips the level prefix (`"1␟Asia"` → `"Asia"`) for display. */
const strip = (name: string, sep: string): string => name.split(sep).pop() ?? name;

export function SankeyViz({ rows, groupKeys, measureKeys }: SankeyVizProps) {
  const measure = measureKeys[0];
  if (!measure || groupKeys.length < 2)
    return <Alert severity="info">Sankey needs at least two group-by dimensions.</Alert>;

  const { nodes, links, sep } = buildSankey(rows, groupKeys, measure);
  if (links.length === 0)
    return <Alert severity="info">沒有可顯示的流向資料（需要正值的 measure）。</Alert>;

  const option: ECOption = {
    color: PALETTE,
    tooltip: {
      trigger: 'item',
      triggerOn: 'mousemove',
      formatter: (p: unknown) => {
        const params = p as {
          dataType?: string;
          name?: string;
          data?: { source?: string; target?: string };
          value?: number;
        };
        if (params.dataType === 'edge' && params.data) {
          return `${strip(params.data.source ?? '', sep)} → ${strip(params.data.target ?? '', sep)}: ${params.value}`;
        }
        return strip(params.name ?? '', sep);
      },
    },
    series: [
      {
        type: 'sankey',
        data: nodes,
        links,
        emphasis: { focus: 'adjacency' },
        nodeAlign: 'justify',
        label: {
          color: '#EEEFF1',
          formatter: (p: unknown) => strip((p as { name?: string }).name ?? '', sep),
        },
        lineStyle: { color: 'gradient', opacity: 0.4 },
      },
    ],
  };

  return <EChart option={option} />;
}
