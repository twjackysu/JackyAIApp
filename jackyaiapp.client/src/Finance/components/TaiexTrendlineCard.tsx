import Alert from '@mui/material/Alert';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';

import { TRENDLINE_CHART_HEIGHT, TrendlineChart } from './TrendlineChart';

import { useGetPriceBarsQuery } from '@/apis/financeApis';

/** Stock Intelligence's 加權指數趨勢線: the TAIEX's last two years with automatic trendlines. */
export const TaiexTrendlineCard = () => {
  const { data: res, isError } = useGetPriceBarsQuery({ code: 'TAIEX' });
  const data = res?.data && res.data.bars.length > 0 ? res.data : null;

  return (
    <Paper
      sx={{
        p: 2,
        mt: 3,
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
        overflow: 'hidden',
      }}
    >
      <Typography variant="h6" component="h2" sx={{ fontWeight: 700 }}>
        加權指數趨勢線
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
        近兩年日K，依波段高低點自動畫出支撐與壓力線
      </Typography>
      {data ? (
        <TrendlineChart data={data} />
      ) : isError ? (
        <Alert severity="warning">加權指數資料暫時無法取得，請稍後再試。</Alert>
      ) : (
        <Skeleton variant="rounded" height={TRENDLINE_CHART_HEIGHT} aria-label="載入中" />
      )}
    </Paper>
  );
};
