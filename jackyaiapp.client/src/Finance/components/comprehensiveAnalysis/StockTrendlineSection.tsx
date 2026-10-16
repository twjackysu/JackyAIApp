import ShowChartIcon from '@mui/icons-material/ShowChart';
import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { PriceBarsTrendline } from '../PriceBarsTrendline';

import type { MarketRegion } from '@/apis/financeApis/types';

/** The analysed stock's recent daily K-line with trendlines. */
export const StockTrendlineSection = ({
  stockCode,
  market,
}: {
  stockCode: string;
  market: MarketRegion;
}) => (
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
    <PriceBarsTrendline
      code={stockCode}
      market={market}
      defaultRange="all"
      unavailableMessage="這檔股票的歷史價格不足或暫時無法取得，無法畫趨勢線。"
    />
  </Box>
);
