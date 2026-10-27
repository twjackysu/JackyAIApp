import Alert from '@mui/material/Alert';

import { PALETTE, type ECOption } from '../../components/summarizeChart/echarts';
import { buildSankey } from '../dataPrep';

import { EChart } from './EChart';
import { sankeyTooltip, stripLevel } from './sankeyTooltip';

// Registers sankey with ECharts; without it the chart renders as an empty canvas.
import '../registerDataAnalysisCharts';

interface SankeyVizProps {
  rows: Record<string, unknown>[];
  groupKeys: string[];
  measureKeys: string[];
}

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
      formatter: (p: unknown) => sankeyTooltip(p, sep),
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
          formatter: (p: unknown) => stripLevel((p as { name?: string }).name ?? '', sep),
        },
        lineStyle: { color: 'gradient', opacity: 0.4 },
      },
    ],
  };

  return <EChart option={option} />;
}
