import RefreshIcon from '@mui/icons-material/Refresh';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Grid from '@mui/material/Grid';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';

import { MarketSummaryCard } from './index';

import type { StrategicInsight } from '@/apis/financeApis/types';

interface MarketSummaryTabProps {
  isAuthenticated: boolean;
  isMarketLoading: boolean;
  isMarketFetching: boolean;
  isMarketError: boolean;
  marketError: unknown;
  filteredData: StrategicInsight[];
  refetch: () => void;
}

export function MarketSummaryTab({
  isAuthenticated,
  isMarketLoading,
  isMarketFetching,
  isMarketError,
  marketError,
  filteredData,
  refetch,
}: MarketSummaryTabProps) {
  if (!isAuthenticated) {
    return (
      <Alert
        severity="info"
        sx={{ mb: 3 }}
        action={
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              window.location.href = '/api/account/login/Google';
            }}
          >
            登入
          </Button>
        }
      >
        今日市場AI摘要需要登入才能使用（會消耗 AI token）
      </Alert>
    );
  }

  return (
    <>
      {(isMarketLoading || isMarketFetching) && (
        <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', p: 5 }}>
          <CircularProgress />
          <Typography variant="h6" sx={{ ml: 2 }}>
            {isMarketFetching && !isMarketLoading
              ? '正在重新載入市場數據...'
              : '正在獲取最新市場數據...'}
          </Typography>
        </Box>
      )}
      {isMarketError && !isMarketFetching && (
        <Alert
          severity="error"
          sx={{ mb: 3 }}
          action={
            <IconButton
              color="inherit"
              size="small"
              onClick={() => refetch()}
              disabled={isMarketFetching}
            >
              <RefreshIcon />
            </IconButton>
          }
        >
          <Typography variant="body1">無法載入市場數據，請點擊重新整理按鈕再試一次</Typography>
          {typeof marketError === 'object' && marketError !== null && 'status' in marketError && (
            <Typography variant="caption" color="text.secondary">
              錯誤詳情: {String((marketError as { status: unknown }).status)}
            </Typography>
          )}
        </Alert>
      )}
      {!isMarketLoading && !isMarketError && filteredData.length === 0 && (
        <Alert severity="info" sx={{ mb: 3 }}>
          沒有找到符合搜尋條件的公司。
        </Alert>
      )}
      <Grid container spacing={3}>
        {filteredData.map((stock) => (
          <Grid
            key={stock.stockCode}
            size={{
              xs: 12,
              sm: 6,
              md: 4,
            }}
          >
            <MarketSummaryCard stock={stock} />
          </Grid>
        ))}
      </Grid>
    </>
  );
}
