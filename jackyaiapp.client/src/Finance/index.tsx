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
import Paper from '@mui/material/Paper';
import TextField from '@mui/material/TextField';
import InputAdornment from '@mui/material/InputAdornment';
import SearchIcon from '@mui/icons-material/Search';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Alert from '@mui/material/Alert';
import RefreshIcon from '@mui/icons-material/Refresh';
import IconButton from '@mui/material/IconButton';
import LinearProgress from '@mui/material/LinearProgress';
import { green, red, blue, grey, orange, purple } from '@mui/material/colors';
import { useGetDailyImportantInfoQuery, useAnalyzeStockMutation } from '@/apis/financeApis';
import { StrategicInsight, StockTrendAnalysis } from '@/apis/financeApis/types';

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

// Helper functions for stock trend analysis
const getTrendColor = (trend: string, alpha: number = 1) => {
  switch (trend) {
    case 'bullish':
      return alpha === 1 ? green[500] : `rgba(76, 175, 80, ${alpha})`;
    case 'bearish':
      return alpha === 1 ? red[500] : `rgba(244, 67, 54, ${alpha})`;
    default:
      return alpha === 1 ? blue[500] : `rgba(33, 150, 243, ${alpha})`;
  }
};

const getTrendIcon = (trend: string) => {
  switch (trend) {
    case 'bullish':
      return <ArrowUpwardIcon />;
    case 'bearish':
      return <ArrowDownwardIcon />;
    default:
      return <HelpOutlineIcon />;
  }
};

const getTrendLabel = (trend: string) => {
  switch (trend) {
    case 'bullish':
      return '看漲';
    case 'bearish':
      return '看跌';
    default:
      return '中性';
  }
};

const getRecommendationColor = (recommendation: string, alpha: number = 1) => {
  switch (recommendation) {
    case 'buy':
      return alpha === 1 ? green[500] : `rgba(76, 175, 80, ${alpha})`;
    case 'sell':
      return alpha === 1 ? red[500] : `rgba(244, 67, 54, ${alpha})`;
    default:
      return alpha === 1 ? orange[500] : `rgba(255, 152, 0, ${alpha})`;
  }
};

const getRecommendationLabel = (recommendation: string) => {
  switch (recommendation) {
    case 'buy':
      return '建議買進';
    case 'sell':
      return '建議賣出';
    default:
      return '建議持有';
  }
};

const getRecommendationChipColor = (recommendation: string): 'success' | 'error' | 'warning' => {
  switch (recommendation) {
    case 'buy':
      return 'success';
    case 'sell':
      return 'error';
    default:
      return 'warning';
  }
};

const getConfidenceLabel = (confidence: string) => {
  switch (confidence) {
    case 'high':
      return '高';
    case 'medium':
      return '中';
    case 'low':
      return '低';
    default:
      return '未知';
  }
};

function Finance() {
  const [searchTerm, setSearchTerm] = useState('');
  const [stockSearchTerm, setStockSearchTerm] = useState('');
  const [searchResults, setSearchResults] = useState<StockTrendAnalysis | null>(null);
  const currentDate = useMemo(() => getCurrentDate(), []);

  // Use RTK Query hook to fetch daily market data
  const {
    data: apiResponse,
    isLoading,
    isError,
    error,
    refetch,
    isFetching,
  } = useGetDailyImportantInfoQuery(undefined, {
    // Always refetch when component mounts or user triggers refetch
    refetchOnMountOrArgChange: true,
    // Don't skip the query
    skip: false,
  });

  // Use mutation hook for stock analysis
  const [analyzeStock, { isLoading: isAnalyzing, error: analysisError, reset: resetAnalysis }] =
    useAnalyzeStockMutation();

  const stockInsights: StrategicInsight[] = apiResponse?.data || [];

  const filteredData = useMemo(() => {
    return stockInsights.filter(
      (item) =>
        item.stockCode.toLowerCase().includes(searchTerm.toLowerCase()) ||
        item.companyName.toLowerCase().includes(searchTerm.toLowerCase()) ||
        item.title.toLowerCase().includes(searchTerm.toLowerCase()),
    );
  }, [searchTerm, stockInsights]);

  // Handle stock search
  const handleStockSearch = async () => {
    if (!stockSearchTerm.trim()) {
      return;
    }

    try {
      resetAnalysis();
      const result = await analyzeStock({ stockCodeOrName: stockSearchTerm.trim() }).unwrap();
      setSearchResults(result.data);
    } catch (error) {
      console.error('Stock analysis failed:', error);
      setSearchResults(null);
    }
  };

  // Handle search on Enter key
  const handleSearchKeyPress = (event: React.KeyboardEvent) => {
    if (event.key === 'Enter') {
      handleStockSearch();
    }
  };

  // Clear search results
  const clearSearchResults = () => {
    setSearchResults(null);
    setStockSearchTerm('');
    resetAnalysis();
  };

  return (
    <Box sx={{ p: 3 }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4" component="h1" fontWeight="bold">
          {searchResults ? '股票分析 (Stock Analysis)' : '今日市場摘要 (Market Summary)'}
        </Typography>
        <Stack direction="row" alignItems="center" spacing={2}>
          <Typography variant="subtitle1" color="text.secondary">
            {currentDate}
          </Typography>
          {!searchResults && (
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
          )}
        </Stack>
      </Stack>

      {/* Stock Search Section */}
      <Paper
        sx={{
          p: 3,
          mb: 3,
          bgcolor: 'background.paper',
          border: '1px solid',
          borderColor: 'divider',
        }}
      >
        <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold', color: 'primary.main' }}>
          股票分析搜尋 (Stock Analysis)
        </Typography>
        <Stack direction="row" spacing={2} alignItems="center">
          <TextField
            fullWidth
            variant="outlined"
            placeholder="輸入股票代碼或公司名稱 (例如: 2330, 台積電)"
            value={stockSearchTerm}
            onChange={(e) => setStockSearchTerm(e.target.value)}
            onKeyPress={handleSearchKeyPress}
            disabled={isAnalyzing}
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon />
                </InputAdornment>
              ),
            }}
            sx={{ flexGrow: 1 }}
          />
          <Button
            variant="contained"
            onClick={handleStockSearch}
            disabled={isAnalyzing || !stockSearchTerm.trim()}
            sx={{ minWidth: 120, height: 56 }}
          >
            {isAnalyzing ? <CircularProgress size={20} color="inherit" /> : '分析'}
          </Button>
          {searchResults && (
            <Button
              variant="outlined"
              onClick={clearSearchResults}
              disabled={isAnalyzing}
              sx={{ height: 56 }}
            >
              清除
            </Button>
          )}
        </Stack>
        {isAnalyzing && (
          <Box sx={{ mt: 2 }}>
            <LinearProgress />
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              正在分析股票資料並產生趨勢預測...這可能需要 1-2 分鐘
            </Typography>
          </Box>
        )}
      </Paper>

      {/* Market Summary Filter - Hidden when stock search results exist */}
      {!searchResults && (
        <Paper sx={{ p: 2, mb: 3 }}>
          <TextField
            fullWidth
            variant="outlined"
            placeholder="搜尋今日市場摘要..."
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
        </Paper>
      )}

      {/* Market Data Loading State - Hidden when stock search results exist */}
      {!searchResults && (isLoading || isFetching) && (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 5 }}>
          <CircularProgress />
          <Typography variant="h6" sx={{ ml: 2 }}>
            {isFetching && !isLoading ? '正在重新載入市場數據...' : '正在獲取最新市場數據...'}
          </Typography>
        </Box>
      )}

      {/* Market Data Error State - Hidden when stock search results exist */}
      {!searchResults && isError && !isFetching && (
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
                錯誤詳情: {error.status} -{' '}
                {error.data ? JSON.stringify(error.data).substring(0, 100) : '伺服器暫時無法回應'}
              </Typography>
            )}
          </Stack>
        </Alert>
      )}

      {/* Stock Analysis Error */}
      {analysisError && !isAnalyzing && (
        <Alert severity="error" sx={{ mb: 3 }}>
          <Stack direction="column" spacing={1}>
            <Typography variant="body1">股票分析失敗，請檢查股票代碼是否正確</Typography>
            {analysisError && 'status' in analysisError && (
              <Typography variant="caption" color="text.secondary">
                錯誤詳情: {analysisError.status} -{' '}
                {analysisError.data
                  ? JSON.stringify(analysisError.data).substring(0, 100)
                  : '分析服務暫時無法使用'}
              </Typography>
            )}
          </Stack>
        </Alert>
      )}

      {/* Stock Analysis Results */}
      {searchResults && (
        <Paper
          sx={{
            p: 3,
            mb: 3,
            bgcolor: 'background.paper',
            border: '2px solid',
            borderColor: 'primary.main',
          }}
        >
          <Typography variant="h5" gutterBottom sx={{ fontWeight: 'bold', color: 'primary.main' }}>
            {searchResults.companyName} ({searchResults.stockCode}) - 趨勢分析
          </Typography>
          {searchResults.currentPrice && (
            <Typography variant="h6" color="text.secondary" gutterBottom>
              當前股價: ${searchResults.currentPrice}
            </Typography>
          )}

          <Grid container spacing={3} sx={{ mt: 1 }}>
            {/* Short Term */}
            <Grid item xs={12} md={4}>
              <Card
                sx={{ height: '100%', bgcolor: getTrendColor(searchResults.shortTermTrend, 0.1) }}
              >
                <CardHeader
                  avatar={
                    <Avatar sx={{ bgcolor: getTrendColor(searchResults.shortTermTrend) }}>
                      {getTrendIcon(searchResults.shortTermTrend)}
                    </Avatar>
                  }
                  title="短期趨勢 (1-3個月)"
                  subheader={getTrendLabel(searchResults.shortTermTrend)}
                />
                <CardContent>
                  <Typography variant="body2">{searchResults.shortTermSummary}</Typography>
                </CardContent>
              </Card>
            </Grid>

            {/* Medium Term */}
            <Grid item xs={12} md={4}>
              <Card
                sx={{ height: '100%', bgcolor: getTrendColor(searchResults.mediumTermTrend, 0.1) }}
              >
                <CardHeader
                  avatar={
                    <Avatar sx={{ bgcolor: getTrendColor(searchResults.mediumTermTrend) }}>
                      {getTrendIcon(searchResults.mediumTermTrend)}
                    </Avatar>
                  }
                  title="中期趨勢 (3-12個月)"
                  subheader={getTrendLabel(searchResults.mediumTermTrend)}
                />
                <CardContent>
                  <Typography variant="body2">{searchResults.mediumTermSummary}</Typography>
                </CardContent>
              </Card>
            </Grid>

            {/* Long Term */}
            <Grid item xs={12} md={4}>
              <Card
                sx={{ height: '100%', bgcolor: getTrendColor(searchResults.longTermTrend, 0.1) }}
              >
                <CardHeader
                  avatar={
                    <Avatar sx={{ bgcolor: getTrendColor(searchResults.longTermTrend) }}>
                      {getTrendIcon(searchResults.longTermTrend)}
                    </Avatar>
                  }
                  title="長期趨勢 (1-3年)"
                  subheader={getTrendLabel(searchResults.longTermTrend)}
                />
                <CardContent>
                  <Typography variant="body2">{searchResults.longTermSummary}</Typography>
                </CardContent>
              </Card>
            </Grid>

            {/* Key Factors */}
            <Grid item xs={12} md={6}>
              <Card sx={{ height: '100%' }}>
                <CardHeader title="關鍵因素" />
                <CardContent>
                  <Stack spacing={1}>
                    {searchResults.keyFactors.map((factor, index) => (
                      <Chip key={index} label={factor} variant="outlined" color="primary" />
                    ))}
                  </Stack>
                </CardContent>
              </Card>
            </Grid>

            {/* Risk Factors */}
            <Grid item xs={12} md={6}>
              <Card sx={{ height: '100%' }}>
                <CardHeader title="風險因素" />
                <CardContent>
                  <Stack spacing={1}>
                    {searchResults.riskFactors.map((risk, index) => (
                      <Chip key={index} label={risk} variant="outlined" color="warning" />
                    ))}
                  </Stack>
                </CardContent>
              </Card>
            </Grid>

            {/* Investment Recommendation */}
            <Grid item xs={12}>
              <Card sx={{ bgcolor: getRecommendationColor(searchResults.recommendation, 0.1) }}>
                <CardHeader
                  title="投資建議"
                  action={
                    <Chip
                      label={getRecommendationLabel(searchResults.recommendation)}
                      color={getRecommendationChipColor(searchResults.recommendation)}
                      variant="filled"
                      sx={{ fontWeight: 'bold' }}
                    />
                  }
                />
                <CardContent>
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Typography variant="body1">
                      信心水平: <strong>{getConfidenceLabel(searchResults.confidenceLevel)}</strong>
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      更新時間: {new Date(searchResults.lastUpdated).toLocaleString('zh-TW')}
                    </Typography>
                  </Stack>
                </CardContent>
              </Card>
            </Grid>
          </Grid>
        </Paper>
      )}

      {/* Market Summary Cards - Hidden when stock search results exist */}
      {!searchResults && (
        <>
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
        </>
      )}
    </Box>
  );
}

export default Finance;
