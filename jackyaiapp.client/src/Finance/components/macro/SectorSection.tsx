import FactoryIcon from '@mui/icons-material/Factory';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import EChartsCoreModule from 'echarts-for-react/lib/core';
import { useMemo } from 'react';

import { formatNumber, formatROCDate } from './format';

import type { SectorIndex } from '@/apis/financeApis/types';
import type { ECOption } from '@/charts/echarts';

import { echarts } from '@/charts/echarts';

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
// an unusually large move from washing out every other bar's color contrast.
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

interface SectorBarDatum {
  name: string;
  value: number; // changePercent — the bar's length AND the informative dimension (no reliable
  // per-sector market-cap weight is available from the TWSE feed this data comes from, so unlike
  // a treemap this chart never needs one: length already encodes magnitude honestly).
  closeIndex: number;
  changePoints: number;
}

const ROW_HEIGHT = 34;
const CHART_PADDING = 40;

export const SectorSection = ({ sectors }: { sectors: SectorIndex[] }) => {
  // Exclude 加權指數 since it's already shown in the main card
  const filtered = useMemo(() => sectors.filter((s) => s.name !== '發行量加權股價指數'), [sectors]);

  // Ascending so the category axis (which renders its first entry at the bottom) puts the best
  // performer at the top — the standard "sector performance" bar-chart convention.
  const sorted = useMemo(
    () => [...filtered].sort((a, b) => a.changePercent - b.changePercent),
    [filtered],
  );

  const option: ECOption = useMemo(() => {
    const data: SectorBarDatum[] = sorted.map((s) => ({
      name: sectorDisplayNames[s.name] ?? s.name,
      value: s.changePercent,
      closeIndex: s.closeIndex,
      changePoints: s.direction === '-' ? -s.changePoints : s.changePoints,
    }));

    return {
      grid: { left: 8, right: 48, top: 8, bottom: 8, containLabel: true },
      xAxis: {
        type: 'value',
        axisLabel: { formatter: '{value}%' },
        splitLine: { show: true },
      },
      yAxis: {
        type: 'category',
        data: data.map((d) => d.name),
        axisTick: { show: false },
      },
      tooltip: {
        trigger: 'item',
        formatter: (params: unknown) => {
          const p = params as { data: SectorBarDatum };
          const d = p.data;
          const sign = d.value > 0 ? '+' : '';
          return [
            `<b>${d.name}</b>`,
            `指數：${formatNumber(Math.round(d.closeIndex))}`,
            `漲跌：${sign}${d.changePoints.toFixed(2)}（${sign}${d.value.toFixed(2)}%）`,
          ].join('<br/>');
        },
      },
      series: [
        {
          type: 'bar',
          data: data.map((d) => ({
            ...d,
            itemStyle: { color: heatColor(d.value), borderRadius: 3 },
            label: {
              show: true,
              position: d.value >= 0 ? 'right' : 'left',
              formatter: () => `${d.value >= 0 ? '+' : ''}${d.value.toFixed(2)}%`,
              color: heatColor(d.value),
              fontWeight: 700,
            },
          })),
          barMaxWidth: 22,
        },
      ],
    };
  }, [sorted]);

  if (filtered.length === 0) return null;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack
          direction="row"
          spacing={0.5}
          sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 0.5 }}
        >
          <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
            <FactoryIcon fontSize="small" color="action" />
            <Typography variant="subtitle2" color="text.secondary">
              產業類指數
            </Typography>
          </Stack>
          {sorted[0]?.date && (
            <Typography variant="caption" color="text.secondary">
              資料日期：{formatROCDate(sorted[0].date)}
            </Typography>
          )}
        </Stack>
        <ReactECharts
          echarts={echarts}
          option={option}
          style={{ height: sorted.length * ROW_HEIGHT + CHART_PADDING, width: '100%' }}
          opts={{ renderer: 'canvas' }}
        />
      </CardContent>
    </Card>
  );
};
