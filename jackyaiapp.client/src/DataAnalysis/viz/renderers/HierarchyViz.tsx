import Alert from '@mui/material/Alert';

import { PALETTE, type ECOption } from '../../components/summarizeChart/echarts';
import { buildHierarchy } from '../dataPrep';

import { EChart } from './EChart';

type HierarchyMode = 'treemap' | 'sunburst' | 'tree';

interface HierarchyVizProps {
  mode: HierarchyMode;
  rows: Record<string, unknown>[];
  groupKeys: string[];
  measureKeys: string[];
}

export function HierarchyViz({ mode, rows, groupKeys, measureKeys }: HierarchyVizProps) {
  const measure = measureKeys[0];
  if (!measure || groupKeys.length === 0)
    return <Alert severity="info">Need at least one group-by and a measure.</Alert>;

  const data = buildHierarchy(rows, groupKeys, measure);

  let option: ECOption;
  if (mode === 'treemap') {
    option = {
      color: PALETTE,
      tooltip: { trigger: 'item', formatter: '{b}: {c}' },
      series: [
        {
          type: 'treemap',
          data,
          roam: false,
          breadcrumb: { show: groupKeys.length > 1, bottom: 4 },
          label: { show: true, overflow: 'truncate' },
          upperLabel: { show: groupKeys.length > 1, height: 18 },
          levels: [
            { itemStyle: { borderColor: 'rgba(0,0,0,0.4)', borderWidth: 2, gapWidth: 2 } },
            { itemStyle: { borderColor: 'rgba(0,0,0,0.25)', borderWidth: 1, gapWidth: 1 } },
          ],
        },
      ],
    };
  } else if (mode === 'sunburst') {
    option = {
      color: PALETTE,
      tooltip: { trigger: 'item', formatter: '{b}: {c}' },
      series: [
        {
          type: 'sunburst',
          data,
          radius: [0, '92%'],
          label: { overflow: 'truncate', minAngle: 8 },
          emphasis: { focus: 'ancestor' },
        },
      ],
    };
  } else {
    option = {
      tooltip: { trigger: 'item', triggerOn: 'mousemove', formatter: '{b}: {c}' },
      series: [
        {
          type: 'tree',
          data: [{ name: measure, children: data }],
          top: '2%',
          left: '10%',
          right: '18%',
          bottom: '2%',
          symbolSize: 8,
          label: { position: 'left', align: 'right', fontSize: 11 },
          leaves: { label: { position: 'right', align: 'left' } },
          expandAndCollapse: true,
          initialTreeDepth: 3,
          animationDurationUpdate: 400,
        },
      ],
    };
  }

  return <EChart option={option} />;
}
