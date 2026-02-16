import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Grid from '@mui/material/Grid';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState, useMemo } from 'react';

import { useGetDailyImportantInfoQuery, useAnalyzeStockMutation } from '@/apis/financeApis';
import { StockTrendAnalysis } from '@/apis/financeApis/types';

import {
  StockSearchSection,
  StockAnalysisResult,
  MarketSummaryCard,
} from './components';
import { getCurrentDate } from './utils/financeHelpers';

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
    refetchOnMountOrArgChange: true,
    skip: false,
  });

  // Use mutation hook for stock analysis
  const [analyzeStock, { isLoading: isAnalyzing, error: analysisError, reset: resetAnalysis }] =
    useAnalyzeStockMutation();

  const stockInsights = apiResponse?.data || [];

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

  // Clear search results
  const clearSearchResults = () => {
    setSearchResults(null);
    setStockSearchTerm('');
    resetAnalysis();
  };

  return (
    <Box sx={{ p: 3 }}>
      {/* Header */}
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
              onClick={() => refetch()}
              disabled={isFetching}
              size="small"
              sx={{
                bgcolor: 'action.hover',
                '&:hover': { bgcolor: 'action.selected' },
              }}
            >
              <RefreshIcon />
            </IconButton>
          )}
        </Stack>
      </Stack>

      {/* Stock Search Section */}
      <StockSearchSection
        stockSearchTerm={stockSearchTerm}
        onSearchTermChange={setStockSearchTerm}
        onSearch={handleStockSearch}
        onClear={clearSearchResults}
        isAnalyzing={isAnalyzing}
        hasResults={!!searchResults}
      />

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

      {/* Market Data Loading State */}
      {!searchResults && (isLoading || isFetching) && (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 5 }}>
          <CircularProgress />
          <Typography variant="h6" sx={{ ml: 2 }}>
            {isFetching && !isLoading ? '正在重新載入市場數據...' : '正在獲取最新市場數據...'}
          </Typography>
        </Box>
      )}

      {/* Market Data Error State */}
      {!searchResults && isError && !isFetching && (
        <Alert
          severity="error"
          sx={{ mb: 3 }}
          action={
            <IconButton
              color="inherit"
              size="small"
              onClick={() => refetch()}
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
      {searchResults && <StockAnalysisResult data={searchResults} />}

      {/* Market Summary Cards */}
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
                <MarketSummaryCard stock={stock} />
              </Grid>
            ))}
          </Grid>
        </>
      )}
    </Box>
  );
}

export default Finance;
