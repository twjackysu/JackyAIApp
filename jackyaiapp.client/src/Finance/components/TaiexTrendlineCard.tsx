import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';

import { PriceBarsTrendline } from './PriceBarsTrendline';

/** Stock Intelligence's 加權指數趨勢線: up to six years of the TAIEX with trendlines. */
export const TaiexTrendlineCard = () => (
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
      日K，依波段高低點自動畫出支撐與壓力線；預設近 1 年，起始日期最早可選到 6
      年前，線也可以自己調整
    </Typography>
    <PriceBarsTrendline
      code="TAIEX"
      defaultRange="1y"
      unavailableMessage="加權指數資料暫時無法取得，請稍後再試。"
    />
  </Paper>
);
