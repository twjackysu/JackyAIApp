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
import { useMemo } from 'react';

import type { IndicatorSeries } from '@/apis/financeApis/types';
import type { ApiErrorResponse } from '@/apis/types';
import type { ECOption } from '@/charts/echarts';

import { useGetIndicatorsQuery, useLazyGetIndicatorInsightQuery } from '@/apis/financeApis';
import ReactECharts from '@/charts/ReactECharts';
import { fmtNum } from '@/utils/format';

const formatValue = (v: number | null): string => {
  if (v == null) return '—';
  // Large values (index/volume-scale) read cleaner with no decimals; smaller ones keep 2.
  const decimals = Math.abs(v) >= 1000 ? 0 : 2;
  return fmtNum(v, decimals);
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

      <ReactECharts option={option} style={{ height: 130, width: '100%' }} />

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
  const { data: apiResponse, isLoading, isError, error } = useGetIndicatorsQuery();
  const indicators = apiResponse?.data?.indicators ?? [];

  // Surface the real server reason instead of always blaming the API key.
  const serverMessage =
    error && typeof error === 'object' && 'data' in error
      ? (error as { data?: ApiErrorResponse }).data?.error?.message
      : undefined;

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
    return (
      <Alert severity="warning">
        {serverMessage || (isError ? '指標資料載入失敗，請稍後再試。' : '目前沒有指標資料。')}
      </Alert>
    );
  }

  return (
    <Box sx={{ mt: 3 }}>
      <Typography variant="h6" sx={{ fontWeight: 700, mb: 0.5 }}>
        關鍵總經指標
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        資料來源：FRED。點「AI 解讀」看白話分析。
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
