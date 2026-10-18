import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { DEFAULT_WIDE_RANGE_DAYS, WIDE_RANGES } from './dayRanges';
import { currentDrawdownStatus } from './drawdownStatus';
import { HistoryLimitNote } from './HistoryLimitNote';
import { RangePicker } from './RangePicker';
import { StatBlock } from './StatBlock';

import { useGetDrawdownQuery } from '@/apis/portfolioApis';
import { ECHARTS_THEME, ECOption } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';
import CenteredSpinner from '@/components/CenteredSpinner';
import { LOSS_COLOR } from '@/utils/format';

export function DrawdownChart() {
  const [days, setDays] = useState(DEFAULT_WIDE_RANGE_DAYS);
  const { data: res, isLoading, isFetching } = useGetDrawdownQuery(days);
  const analysis = res?.data;
  const points = analysis?.points ?? [];

  if (isLoading) {
    return <CenteredSpinner sx={{ py: 6 }} />;
  }

  if (points.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        資料不足，更新持倉後即可看到回撤分析
      </Typography>
    );
  }

  const hasDrawdownEpisode = analysis!.maxDrawdownPct < 0;
  const status = currentDrawdownStatus(analysis!.currentDrawdownPct);

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
        lineStyle: { width: 1.5, color: LOSS_COLOR },
        itemStyle: { color: LOSS_COLOR },
        areaStyle: { color: LOSS_COLOR, opacity: 0.35 },
      },
    ],
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1 }}>
        <RangePicker ranges={WIDE_RANGES} value={days} onChange={setDays} />
      </Box>

      <HistoryLimitNote days={days} />

      {isFetching && <LinearProgress sx={{ mb: 1 }} />}

      <Grid container spacing={2} sx={{ mb: 2 }}>
        <Grid size={{ xs: 6, sm: 3 }}>
          <StatBlock
            label="最大回撤"
            value={`${analysis!.maxDrawdownPct.toFixed(2)}%`}
            color={hasDrawdownEpisode ? LOSS_COLOR : undefined}
          />
        </Grid>
        <Grid size={{ xs: 6, sm: 3 }}>
          <StatBlock label="目前回撤" value={`${analysis!.currentDrawdownPct.toFixed(2)}%`} />
        </Grid>
        {hasDrawdownEpisode && (
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatBlock
              label="水下天數"
              value={analysis!.underwaterDays != null ? `${analysis!.underwaterDays} 天` : '—'}
            />
          </Grid>
        )}
        <Grid size={{ xs: 6, sm: 3 }}>
          <StatBlock label="目前狀態">
            {/* Reports NOW, not whether the worst episode recovered — see drawdownStatus.ts. A
                green "已復原" badge sitting next to a negative 目前回撤 was self-contradictory:
                the episode can be long resolved while net worth has since drifted back down. The
                episode's own resolution is in the 高點 → 低點 → 復原於 line below. */}
            <Chip label={status.label} size="small" color={status.color} sx={{ mt: 0.5 }} />
          </StatBlock>
        </Grid>
      </Grid>

      {hasDrawdownEpisode && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1.5 }}>
          高點 {analysis!.peakDate} → 低點 {analysis!.troughDate}
          {analysis!.recoveryDate ? ` → 復原於 ${analysis!.recoveryDate}` : '（尚未回到前高）'}
        </Typography>
      )}

      <ReactECharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 220 }} />

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
        依持倉快照與歷史價格計算——兩次快照之間僅價格變動，數量不變，反映的是「所持部位的價格回撤」而非交易調整後的實際報酬。
      </Typography>
    </Box>
  );
}
