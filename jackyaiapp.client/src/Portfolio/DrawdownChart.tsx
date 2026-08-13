import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Grid from '@mui/material/Grid';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';
import { useState } from 'react';

import { WIDE_RANGES } from './dayRanges';
import { RangePicker } from './RangePicker';

import { useGetDrawdownQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME, ECOption } from '@/charts/echarts';

function Stat({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {label}
      </Typography>
      <Typography variant="body1" sx={{ fontWeight: 700, color }}>
        {value}
      </Typography>
    </Box>
  );
}

export function DrawdownChart() {
  const [days, setDays] = useState(365);
  const { data: res, isLoading, isFetching } = useGetDrawdownQuery(days);
  const analysis = res?.data;
  const points = analysis?.points ?? [];

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (points.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        資料不足，更新持倉後即可看到回撤分析
      </Typography>
    );
  }

  const hasDrawdownEpisode = analysis!.maxDrawdownPct < 0;

  const option: ECOption = {
    tooltip: {
      trigger: 'axis',
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = (params as { axisValue: string; value: number }[])[0];
        if (!p) return '';
        return `<b>${p.axisValue}</b><br/>回撤：${p.value.toFixed(2)}%`;
      },
    },
    grid: { top: 16, right: 16, bottom: 24, left: 48 },
    xAxis: {
      type: 'category',
      data: points.map((p) => p.date),
      boundaryGap: false,
    },
    yAxis: {
      type: 'value',
      max: 0,
      axisLabel: { formatter: (v: number) => `${v}%` },
    },
    series: [
      {
        name: '回撤',
        type: 'line',
        data: points.map((p) => p.drawdownPct),
        smooth: true,
        symbol: 'none',
        lineStyle: { width: 1.5, color: '#ef4444' },
        itemStyle: { color: '#ef4444' },
        areaStyle: { color: '#ef4444', opacity: 0.35 },
      },
    ],
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1 }}>
        <RangePicker ranges={WIDE_RANGES} value={days} onChange={setDays} />
      </Box>

      {isFetching && <LinearProgress sx={{ mb: 1 }} />}

      <Grid container spacing={2} sx={{ mb: 2 }}>
        <Grid size={{ xs: 6, sm: 3 }}>
          <Stat
            label="最大回撤"
            value={`${analysis!.maxDrawdownPct.toFixed(2)}%`}
            color={hasDrawdownEpisode ? '#ef4444' : undefined}
          />
        </Grid>
        <Grid size={{ xs: 6, sm: 3 }}>
          <Stat label="目前回撤" value={`${analysis!.currentDrawdownPct.toFixed(2)}%`} />
        </Grid>
        {hasDrawdownEpisode && (
          <>
            <Grid size={{ xs: 6, sm: 3 }}>
              <Stat
                label="水下天數"
                value={analysis!.underwaterDays != null ? `${analysis!.underwaterDays} 天` : '—'}
              />
            </Grid>
            <Grid size={{ xs: 6, sm: 3 }}>
              <Box>
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                  復原狀態
                </Typography>
                {analysis!.stillUnderwater ? (
                  <Chip label="尚未復原" size="small" color="warning" sx={{ mt: 0.5 }} />
                ) : (
                  <Chip label="已復原" size="small" color="success" sx={{ mt: 0.5 }} />
                )}
              </Box>
            </Grid>
          </>
        )}
      </Grid>

      {hasDrawdownEpisode && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1.5 }}>
          高點 {analysis!.peakDate} → 低點 {analysis!.troughDate}
          {analysis!.recoveryDate ? ` → 復原於 ${analysis!.recoveryDate}` : '（尚未回到前高）'}
        </Typography>
      )}

      <ReactEcharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 220 }} />

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
        依持倉快照與歷史價格計算——兩次快照之間僅價格變動，數量不變，反映的是「所持部位的價格回撤」而非交易調整後的實際報酬。
      </Typography>
    </Box>
  );
}
