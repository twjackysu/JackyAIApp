import ShowChartIcon from '@mui/icons-material/ShowChart';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { TRENDLINE_CHART_HEIGHT, TrendlineChart } from '../TrendlineChart';

import type { MarketRegion } from '@/apis/financeApis/types';

import { useGetPriceBarsQuery } from '@/apis/financeApis';

/** The analysed stock's recent daily K-line with automatic trendlines. */
export const StockTrendlineSection = ({
  stockCode,
  market,
}: {
  stockCode: string;
  market: MarketRegion;
}) => {
  const { data: res, isError } = useGetPriceBarsQuery({ code: stockCode, market });
  const data = res?.data && res.data.bars.length > 0 ? res.data : null;

  return (
    <Box sx={{ mb: 3 }}>
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 1 }}>
        <ShowChartIcon fontSize="small" />
        <Typography variant="h6" sx={{ fontWeight: 'bold' }}>
          K 線與趨勢線
        </Typography>
        <Typography variant="body2" color="text.secondary">
          近半年日K
        </Typography>
      </Stack>
      {data ? (
        <TrendlineChart data={data} />
      ) : isError ? (
        <Alert severity="info">這檔股票的歷史價格不足或暫時無法取得，無法畫趨勢線。</Alert>
      ) : (
        <Skeleton variant="rounded" height={TRENDLINE_CHART_HEIGHT} aria-label="載入中" />
      )}
    </Box>
  );
};
