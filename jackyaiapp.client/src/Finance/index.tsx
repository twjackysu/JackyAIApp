import { useState, useMemo } from 'react';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CardHeader from '@mui/material/CardHeader';
import Typography from '@mui/material/Typography';
import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Avatar from '@mui/material/Avatar';
import Stack from '@mui/material/Stack';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import HelpOutlineIcon from '@mui/icons-material/HelpOutline';
// import Paper from '@mui/material/Paper';
// import TextField from '@mui/material/TextField';
// import InputAdornment from '@mui/material/InputAdornment';
// import SearchIcon from '@mui/icons-material/Search';
import CircularProgress from '@mui/material/CircularProgress';
import Alert from '@mui/material/Alert';
import RefreshIcon from '@mui/icons-material/Refresh';
import IconButton from '@mui/material/IconButton';
import { green, red, blue, grey } from '@mui/material/colors';
import { useGetDailyImportantInfoQuery } from '@/apis/financeApis';
import { StrategicInsight } from '@/apis/financeApis/types';

// Helper function to get the current date in YYYY-MM-DD format
const getCurrentDate = () => {
  const date = new Date();
  return date.toISOString().split('T')[0];
};

// Helper function to get avatar color based on implication
const getImplicationColor = (implication: string) => {
  switch (implication) {
    case 'bullish':
      return green[500];
    case 'bearish':
      return red[500];
    default:
      return blue[500];
  }
};

// Helper function to get icon based on implication
const ImplicationIcon = ({ implication }: { implication: string }) => {
  switch (implication) {
    case 'bullish':
      return <ArrowUpwardIcon />;
    case 'bearish':
      return <ArrowDownwardIcon />;
    default:
      return <HelpOutlineIcon />;
  }
};

function Finance() {
  const [searchTerm /*, setSearchTerm */] = useState('');
  const currentDate = useMemo(() => getCurrentDate(), []);

  // Use RTK Query hook to fetch data
  const { data: apiResponse, isLoading, isError, error, refetch, isFetching } = useGetDailyImportantInfoQuery(undefined, {
    // Always refetch when component mounts or user triggers refetch
    refetchOnMountOrArgChange: true,
    // Don't skip the query
    skip: false,
  });

  const stockInsights: StrategicInsight[] = apiResponse?.data || [];

  const filteredData = useMemo(() => {
    return stockInsights.filter(
      (item) =>
        item.stockCode.toLowerCase().includes(searchTerm.toLowerCase()) ||
        item.companyName.toLowerCase().includes(searchTerm.toLowerCase()) ||
        item.title.toLowerCase().includes(searchTerm.toLowerCase()),
    );
  }, [searchTerm, stockInsights]);

  return (
    <Box sx={{ p: 3 }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4" component="h1" fontWeight="bold">
          今日市場摘要 (Market Summary)
        </Typography>
        <Stack direction="row" alignItems="center" spacing={2}>
          <Typography variant="subtitle1" color="text.secondary">
            {currentDate}
          </Typography>
          <IconButton
            onClick={() => {
              console.log('Header refresh triggered');
              refetch();
            }}
            disabled={isFetching}
            size="small"
            sx={{
              bgcolor: 'action.hover',
              '&:hover': {
                bgcolor: 'action.selected',
              },
            }}
          >
            <RefreshIcon />
          </IconButton>
        </Stack>
      </Stack>
      {/* 
      <Paper sx={{ p: 2, mb: 3 }}>
        <TextField
          fullWidth
          variant="outlined"
          placeholder="搜尋股票代碼、公司名稱或標題..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          InputProps={{
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon />
              </InputAdornment>
            ),
          }}
          sx={{ mb: 1 }}
        />
      </Paper> */}

      {(isLoading || isFetching) && (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 5 }}>
          <CircularProgress />
          <Typography variant="h6" sx={{ ml: 2 }}>
            {isFetching && !isLoading ? '正在重新載入市場數據...' : '正在獲取最新市場數據...'}
          </Typography>
        </Box>
      )}

      {isError && !isFetching && (
        <Alert
          severity="error"
          sx={{ mb: 3 }}
          action={
            <IconButton
              color="inherit"
              size="small"
              onClick={() => {
                console.log('Manual refetch triggered');
                refetch();
              }}
              disabled={isFetching}
              sx={{
                animation: isFetching ? 'none' : 'pulse 1.5s infinite',
                '@keyframes pulse': {
                  '0%': { transform: 'scale(1)' },
                  '50%': { transform: 'scale(1.1)' },
                  '100%': { transform: 'scale(1)' },
                },
              }}
            >
              <RefreshIcon />
            </IconButton>
          }
        >
          <Stack direction="column" spacing={1}>
            <Typography variant="body1">無法載入市場數據，請點擊重新整理按鈕再試一次</Typography>
            {error && 'status' in error && (
              <Typography variant="caption" color="text.secondary">
                錯誤詳情: {error.status} - {error.data ? JSON.stringify(error.data).substring(0, 100) : '伺服器暫時無法回應'}
              </Typography>
            )}
          </Stack>
        </Alert>
      )}

      {!isLoading && !isError && filteredData.length === 0 && (
        <Alert severity="info" sx={{ mb: 3 }}>
          沒有找到符合搜尋條件的公司。
        </Alert>
      )}

      <Grid container spacing={3}>
        {filteredData.map((stock) => (
          <Grid item xs={12} sm={6} md={4} key={stock.stockCode}>
            <Card
              elevation={3}
              sx={{
                height: '100%',
                display: 'flex',
                flexDirection: 'column',
                transition: 'transform 0.2s',
                '&:hover': {
                  transform: 'translateY(-4px)',
                },
              }}
            >
              <CardHeader
                avatar={
                  <Avatar
                    sx={{
                      bgcolor: getImplicationColor(stock.implication),
                      width: 56,
                      height: 56,
                    }}
                  >
                    {stock.stockCode}
                  </Avatar>
                }
                title={
                  <Typography variant="h6" component="div">
                    {stock.companyName}
                  </Typography>
                }
                subheader={`股票代碼: ${stock.stockCode}`}
                action={
                  <Chip
                    icon={<ImplicationIcon implication={stock.implication} />}
                    label={
                      stock.implication === 'bullish'
                        ? '看漲'
                        : stock.implication === 'bearish'
                          ? '看跌'
                          : '中性'
                    }
                    color={
                      stock.implication === 'bullish'
                        ? 'success'
                        : stock.implication === 'bearish'
                          ? 'error'
                          : 'info'
                    }
                    variant="filled"
                    sx={{ fontWeight: 'bold', mt: 1 }}
                  />
                }
              />

              <CardContent sx={{ flexGrow: 1 }}>
                <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
                  {stock.title}
                </Typography>

                <Typography variant="body2" color="text.secondary" paragraph>
                  {stock.summary}
                </Typography>

                {stock.suggestedAction && (
                  <>
                    <Divider sx={{ my: 1.5 }} />
                    <Box sx={{ mt: 2 }}>
                      <Typography variant="subtitle2" fontWeight="bold" color="primary">
                        投資建議:
                      </Typography>
                      <Typography variant="body2">{stock.suggestedAction}</Typography>
                    </Box>
                  </>
                )}
              </CardContent>

              <Box
                sx={{
                  bgcolor: grey[900],
                  p: 1,
                  borderTop: 1,
                  borderColor: 'divider',
                  display: 'flex',
                  justifyContent: 'flex-end',
                }}
              >
                <Typography variant="caption" color="text.secondary">
                  發布日期: {stock.date}
                </Typography>
              </Box>
            </Card>
          </Grid>
        ))}
      </Grid>
    </Box>
  );
}

export default Finance;
