import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import { green, grey, red } from '@mui/material/colors';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import EChartsCoreModule from 'echarts-for-react/lib/core';
import { useMemo } from 'react';

import type { IndicatorSeries } from '@/apis/financeApis/types';
import type { ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';

import { useGetIndicatorsQuery, useLazyGetIndicatorInsightQuery } from '@/apis/financeApis';
import { echarts } from '@/DataAnalysis/components/summarizeChart/echarts';

// echarts-for-react ships CJS only; the default import may arrive wrapped.
const ReactECharts =
  (EChartsCoreModule as unknown as { default?: typeof EChartsCoreModule }).default ??
  EChartsCoreModule;

const formatValue = (v: number | null): string => {
  if (v == null) return '—';
  const decimals = Math.abs(v) >= 1000 ? 0 : 2;
  return v.toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
};

const IndicatorCard = ({ series }: { series: IndicatorSeries }) => {
  const [fetchInsight, { data: insightRes, isFetching: insightLoading, isError: insightError }] =
    useLazyGetIndicatorInsightQuery();
  const insight = insightRes?.data?.insight;

  const change =
    series.latestValue != null && series.previousValue != null
      ? series.latestValue - series.previousValue
      : null;
  const changeColor =
    change == null ? grey[600] : change > 0 ? green[700] : change < 0 ? red[700] : grey[600];
  const changeSign = change != null && change > 0 ? '+' : '';

  const option: ECOption = useMemo(
    () => ({
      grid: { left: 6, right: 6, top: 8, bottom: 6, containLabel: true },
      tooltip: { trigger: 'axis' },
      xAxis: { type: 'category', data: series.points.map((p) => p.date), show: false },
      yAxis: { type: 'value', scale: true, axisLabel: { fontSize: 10 } },
      series: [
        {
          type: 'line',
          data: series.points.map((p) => p.value),
          showSymbol: false,
          smooth: true,
          lineStyle: { width: 2, color: '#5E6AD2' },
          areaStyle: { color: 'rgba(94,106,210,0.12)' },
        },
      ],
    }),
    [series.points],
  );

  return (
    <Paper
      sx={{ p: 2, height: '100%', border: '1px solid', borderColor: 'divider', borderRadius: 2 }}
    >
      <Box
        sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 1 }}
      >
        <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
          {series.name}
        </Typography>
        <Box sx={{ textAlign: 'right' }}>
          <Typography variant="h6" component="span" sx={{ fontWeight: 700 }}>
            {formatValue(series.latestValue)}
            {series.unit === '%' ? '%' : ''}
          </Typography>
          {change != null && (
            <Typography variant="caption" sx={{ ml: 0.75, fontWeight: 600, color: changeColor }}>
              {changeSign}
              {formatValue(change)}
            </Typography>
          )}
        </Box>
      </Box>

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5 }}>
        {series.description}
        {series.unit && series.unit !== '%' ? `（單位：${series.unit}）` : ''}
      </Typography>

      <ReactECharts
        echarts={echarts}
        option={option}
        style={{ height: 130, width: '100%' }}
        opts={{ renderer: 'canvas' }}
      />

      <Box sx={{ mt: 1 }}>
        {insight ? (
          <Alert severity="info" icon={<AutoAwesomeIcon fontSize="inherit" />} sx={{ py: 0.5 }}>
            {insight}
          </Alert>
        ) : (
          <Button
            size="small"
            startIcon={insightLoading ? <CircularProgress size={14} /> : <AutoAwesomeIcon />}
            disabled={insightLoading}
            onClick={() => fetchInsight(series.id)}
          >
            {insightLoading ? 'AI 解讀中…' : 'AI 解讀'}
          </Button>
        )}
        {insightError && (
          <Typography variant="caption" color="error" sx={{ display: 'block', mt: 0.5 }}>
            解讀失敗，請稍後再試。
          </Typography>
        )}
      </Box>
    </Paper>
  );
};

export const IndicatorExplorer = () => {
  const { data: apiResponse, isLoading, isError } = useGetIndicatorsQuery();
  const indicators = apiResponse?.data?.indicators ?? [];

  if (isLoading) {
    return (
      <Grid container spacing={2}>
        {Array.from({ length: 4 }).map((_, i) => (
          <Grid key={i} size={{ xs: 12, md: 6 }}>
            <Skeleton variant="rounded" height={240} />
          </Grid>
        ))}
      </Grid>
    );
  }

  if (isError || indicators.length === 0) {
    return <Alert severity="warning">指標資料目前無法載入（可能尚未設定 FRED API key）。</Alert>;
  }

  return (
    <Box>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        關鍵總體經濟與市場指標（資料來源：FRED）。點「AI 解讀」看白話分析。
      </Typography>
      <Grid container spacing={2}>
        {indicators.map((series) => (
          <Grid key={series.id} size={{ xs: 12, md: 6 }}>
            <IndicatorCard series={series} />
          </Grid>
        ))}
      </Grid>
    </Box>
  );
};

export default IndicatorExplorer;
