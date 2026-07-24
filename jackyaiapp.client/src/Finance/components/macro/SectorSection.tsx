import FactoryIcon from '@mui/icons-material/Factory';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import EChartsCoreModule from 'echarts-for-react/lib/core';
import { useMemo } from 'react';

import { formatNumber } from './format';

import type { SectorIndex } from '@/apis/financeApis/types';
import type { ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

import { echarts } from '@/DataAnalysis/components/summarizeChart/echarts';

// echarts-for-react ships CJS only; the default import may arrive wrapped.
const ReactECharts =
  (EChartsCoreModule as unknown as { default?: typeof EChartsCoreModule }).default ??
  EChartsCoreModule;

const sectorDisplayNames: Record<string, string> = {
  發行量加權股價指數: '加權指數',
  半導體類指數: '半導體',
  電子工業類指數: '電子工業',
  金融保險類指數: '金融保險',
  航運類指數: '航運',
  臺灣50指數: '台灣50',
  生技醫療類指數: '生技醫療',
  通信網路類指數: '通信網路',
};

// Diverging red/green by magnitude, interpolated from a neutral grey — matches this codebase's
// existing convention (atoms.tsx: positive=green/negative=red), not the TW-local red-up
// convention. Clamped at ±3%: TW sector-index daily moves rarely exceed that, so clamping keeps
// an unusually large move from washing out every other tile's color contrast.
const HEAT_CLAMP_PCT = 3;
const HEAT_NEUTRAL: [number, number, number] = [148, 163, 184]; // slate-400
const HEAT_UP: [number, number, number] = [34, 197, 94]; // green-500
const HEAT_DOWN: [number, number, number] = [239, 68, 68]; // red-500

function heatColor(changePercent: number): string {
  const clamped = Math.max(-HEAT_CLAMP_PCT, Math.min(HEAT_CLAMP_PCT, changePercent));
  const t = Math.abs(clamped) / HEAT_CLAMP_PCT;
  const target = clamped >= 0 ? HEAT_UP : HEAT_DOWN;
  const [r, g, b] = HEAT_NEUTRAL.map((base, i) => Math.round(base + (target[i] - base) * t));
  return `rgb(${r}, ${g}, ${b})`;
}

interface SectorTreemapDatum {
  name: string;
  value: number;
  closeIndex: number;
  changePoints: number;
  changePercent: number;
  itemStyle: { color: string };
}

export const SectorSection = ({ sectors }: { sectors: SectorIndex[] }) => {
  // Exclude 加權指數 since it's already shown in the main card
  const filtered = useMemo(() => sectors.filter((s) => s.name !== '發行量加權股價指數'), [sectors]);

  const option: ECOption = useMemo(() => {
    const data: SectorTreemapDatum[] = filtered.map((s) => ({
      name: sectorDisplayNames[s.name] ?? s.name,
      // No per-sector market-cap weight is available from the TWSE feed this data comes from, so
      // every tile gets equal area rather than implying a weighting accuracy we don't have — this
      // is a heatmap grid (color = the informative dimension), not a cap-weighted treemap.
      value: 1,
      closeIndex: s.closeIndex,
      changePoints: s.direction === '-' ? -s.changePoints : s.changePoints,
      changePercent: s.changePercent,
      itemStyle: { color: heatColor(s.changePercent) },
    }));

    return {
      tooltip: {
        formatter: (params: unknown) => {
          const p = params as { data: SectorTreemapDatum };
          const d = p.data;
          const sign = d.changePercent > 0 ? '+' : '';
          return [
            `<b>${d.name}</b>`,
            `指數：${formatNumber(Math.round(d.closeIndex))}`,
            `漲跌：${sign}${d.changePoints.toFixed(2)}（${sign}${d.changePercent.toFixed(2)}%）`,
          ].join('<br/>');
        },
      },
      series: [
        {
          type: 'treemap',
          data,
          roam: false,
          nodeClick: false,
          breadcrumb: { show: false },
          upperLabel: { show: false },
          itemStyle: { borderColor: 'transparent', borderWidth: 2, gapWidth: 2 },
          label: {
            show: true,
            color: '#fff',
            fontWeight: 700,
            fontSize: 13,
            formatter: (params: unknown) => {
              const p = params as { data: SectorTreemapDatum };
              const sign = p.data.changePercent > 0 ? '+' : '';
              return `{name|${p.data.name}}\n{pct|${sign}${p.data.changePercent.toFixed(2)}%}`;
            },
            rich: {
              name: { fontSize: 13, fontWeight: 700, lineHeight: 18 },
              pct: { fontSize: 12, lineHeight: 16 },
            },
          },
        },
      ],
    };
  }, [filtered]);

  if (filtered.length === 0) return null;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 0.5 }}>
          <FactoryIcon fontSize="small" color="action" />
          <Typography variant="subtitle2" color="text.secondary">
            產業類指數
          </Typography>
        </Stack>
        <ReactECharts
          echarts={echarts}
          option={option}
          style={{ height: 260, width: '100%' }}
          opts={{ renderer: 'canvas' }}
        />
      </CardContent>
    </Card>
  );
};
