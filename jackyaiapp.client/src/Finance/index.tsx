import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Grid from '@mui/material/Grid';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import LinearProgress from '@mui/material/LinearProgress';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState, useMemo } from 'react';

import {
  useGetDailyImportantInfoQuery,
  useAnalyzeStockMutation,
  useGetComprehensiveAnalysisMutation,
} from '@/apis/financeApis';
import { useGetUserInfoQuery } from '@/apis/accountApis';
import { StockTrendAnalysis, StockAnalysisResultData } from '@/apis/financeApis/types';

import {
  StockAnalysisResult,
  MarketSummaryCard,
  ComprehensiveAnalysisResult,
  AnalysisConfigPanel,
  MacroEconomyOverview,
} from './components';
import type { AnalysisConfig } from './components';
import { getCurrentDate } from './utils/financeHelpers';

/** Detect market from stock code: numeric (with optional trailing letter) = TW, otherwise US */
const detectMarket = (code: string): 'TW' | 'US' => /^\d{4,6}[A-Za-z]?$/.test(code.trim()) ? 'TW' : 'US';

// Tab indices
const TAB_QUANTITATIVE = 0;
const TAB_AI_TREND = 1;
const TAB_MARKET_SUMMARY = 2;

function Finance() {
  const [searchTerm, setSearchTerm] = useState('');
  const [stockSearchTerm, setStockSearchTerm] = useState('');
  const [searchResults, setSearchResults] = useState<StockTrendAnalysis | null>(null);
  const [analysisResults, setAnalysisResults] = useState<StockAnalysisResultData | null>(null);
  const [analysisTab, setAnalysisTab] = useState<number>(TAB_QUANTITATIVE);
  const [analysisConfig, setAnalysisConfig] = useState<AnalysisConfig>({
    includeTechnical: true,
    includeChip: true,
    includeFundamental: true,
    includeScoring: true,
    includeRisk: true,
    technicalWeight: 0.5,
    chipWeight: 0.3,
    fundamentalWeight: 0.2,
  });
  const currentDate = useMemo(() => getCurrentDate(), []);
  const { data: userInfo } = useGetUserInfoQuery();
  const isAuthenticated = !!userInfo?.data;

  const {
    data: apiResponse,
    isLoading: isMarketLoading,
    isError: isMarketError,
    error: marketError,
    refetch,
    isFetching: isMarketFetching,
  } = useGetDailyImportantInfoQuery(undefined, {
    refetchOnMountOrArgChange: true,
    // Only fetch when market summary tab is active AND user is authenticated
    skip: analysisTab !== TAB_MARKET_SUMMARY || !isAuthenticated,
  });

  const [analyzeStock, { isLoading: isAnalyzing, error: analysisError, reset: resetAnalysis }] =
    useAnalyzeStockMutation();

  const [
    getComprehensiveAnalysis,
    { isLoading: isComprehensiveLoading, error: comprehensiveError, reset: resetComprehensive },
  ] = useGetComprehensiveAnalysisMutation();

  const stockInsights = apiResponse?.data || [];
  const filteredData = useMemo(() => {
    return stockInsights.filter(
      (item) =>
        item.stockCode.toLowerCase().includes(searchTerm.toLowerCase()) ||
        item.companyName.toLowerCase().includes(searchTerm.toLowerCase()) ||
        item.title.toLowerCase().includes(searchTerm.toLowerCase()),
    );
  }, [searchTerm, stockInsights]);

  const handleStockSearch = async () => {
    if (!stockSearchTerm.trim()) return;

    if (analysisTab === TAB_QUANTITATIVE) {
      try {
        resetComprehensive();
        setSearchResults(null);
        const code = stockSearchTerm.trim();
        const market = detectMarket(code);
        const isUS = market === 'US';
        const result = await getComprehensiveAnalysis({
          stockCode: code,
          market,
          includeTechnical: analysisConfig.includeTechnical,
          includeChip: isUS ? false : analysisConfig.includeChip,
          includeFundamental: analysisConfig.includeFundamental,
          includeScoring: analysisConfig.includeScoring,
          includeRisk: analysisConfig.includeRisk,
          technicalWeight: analysisConfig.technicalWeight,
          chipWeight: isUS ? 0 : analysisConfig.chipWeight,
          fundamentalWeight: analysisConfig.fundamentalWeight,
        }).unwrap();
        setAnalysisResults(result.data);
      } catch (err) {
        console.error('Comprehensive analysis failed:', err);
        setAnalysisResults(null);
      }
    } else if (analysisTab === TAB_AI_TREND) {
      if (!isAuthenticated) {
        window.location.href = `/api/account/login/Google?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
        return;
      }
      try {
        resetAnalysis();
        setAnalysisResults(null);
        const result = await analyzeStock({ stockCodeOrName: stockSearchTerm.trim() }).unwrap();
        setSearchResults(result.data);
      } catch (err) {
        console.error('Stock analysis failed:', err);
        setSearchResults(null);
      }
    }
  };

  const clearSearchResults = () => {
    setSearchResults(null);
    setAnalysisResults(null);
    setStockSearchTerm('');
    resetAnalysis();
    resetComprehensive();
  };

  const handleTabChange = (_: unknown, newTab: number) => {
    setAnalysisTab(newTab);
    // Clear results when switching tabs
    clearSearchResults();
  };

  const hasStockResults = !!searchResults || !!analysisResults;
  const isAnyAnalyzing = isAnalyzing || isComprehensiveLoading;
  const isSearchTab = analysisTab === TAB_QUANTITATIVE || analysisTab === TAB_AI_TREND;

  return (
    <Box sx={{ p: 3 }}>
      {/* Header */}
      <Stack direction="row" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4" component="h1" fontWeight="bold">
          📈 Finance Dashboard
        </Typography>
        <Typography variant="subtitle1" color="text.secondary">{currentDate}</Typography>
      </Stack>

      {/* Macro Economy Overview — shown before search results */}
      {!hasStockResults && <MacroEconomyOverview />}

      {/* Tabs */}
      <Paper sx={{ p: 3, mb: 3, border: '1px solid', borderColor: 'divider' }}>
        <Tabs value={analysisTab} onChange={handleTabChange} variant="scrollable" scrollButtons="auto" allowScrollButtonsMobile sx={{ mb: 2 }}>
          <Tab label="📊 綜合量化分析" />
          <Tab label="🤖 AI 趨勢分析" />
          <Tab label="📰 今日市場AI摘要" />
        </Tabs>

        {/* Login prompt for AI tabs */}
        {!isAuthenticated && analysisTab === TAB_AI_TREND && (
          <Alert severity="info" sx={{ mb: 2 }} action={
            <Button color="inherit" size="small" onClick={() => { window.location.href = '/api/account/login/Google'; }}>登入</Button>
          }>
            AI 趨勢分析需要登入才能使用（會消耗 AI token）
          </Alert>
        )}

        {/* Search bar for stock analysis tabs */}
        {isSearchTab && (
          <>
            <Stack direction="row" spacing={2} alignItems="center">
              <TextField
                fullWidth variant="outlined"
                placeholder="輸入股票代碼 (例如: 2330, 台積電, AAPL, TSLA)"
                value={stockSearchTerm}
                onChange={(e) => setStockSearchTerm(e.target.value)}
                onKeyPress={(e) => e.key === 'Enter' && handleStockSearch()}
                disabled={isAnyAnalyzing || (analysisTab === TAB_AI_TREND && !isAuthenticated)}
                InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> }}
                sx={{ flexGrow: 1 }}
              />
              <Button variant="contained" onClick={handleStockSearch} disabled={isAnyAnalyzing || !stockSearchTerm.trim() || (analysisTab === TAB_AI_TREND && !isAuthenticated)} sx={{ minWidth: 120, height: 56 }}>
                {isAnyAnalyzing ? <CircularProgress size={20} color="inherit" /> : '分析'}
              </Button>
              {hasStockResults && (
                <Button variant="outlined" onClick={clearSearchResults} disabled={isAnyAnalyzing} sx={{ height: 56 }}>清除</Button>
              )}
            </Stack>
            {isAnyAnalyzing && (
              <Box sx={{ mt: 2 }}>
                <LinearProgress />
                <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                  {analysisTab === TAB_AI_TREND
                    ? '正在分析股票資料並產生趨勢預測...這可能需要 1-2 分鐘'
                    : '正在計算技術指標與分析...'}
                </Typography>
              </Box>
            )}
            {analysisTab === TAB_QUANTITATIVE && (
              <Box sx={{ mt: 2 }}><AnalysisConfigPanel config={analysisConfig} onChange={setAnalysisConfig} /></Box>
            )}
          </>
        )}

        {/* Market summary search filter */}
        {analysisTab === TAB_MARKET_SUMMARY && !isMarketLoading && !isMarketError && stockInsights.length > 0 && (
          <Stack direction="row" spacing={2} alignItems="center">
            <TextField
              fullWidth variant="outlined"
              placeholder="搜尋市場摘要..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> }}
            />
            <IconButton onClick={() => refetch()} disabled={isMarketFetching} sx={{ bgcolor: 'action.hover', '&:hover': { bgcolor: 'action.selected' } }}>
              <RefreshIcon />
            </IconButton>
          </Stack>
        )}
      </Paper>

      {/* Error states */}
      {analysisError && !isAnalyzing && (
        <Alert severity="error" sx={{ mb: 3 }}>股票分析失敗，請檢查股票代碼是否正確</Alert>
      )}
      {comprehensiveError && !isComprehensiveLoading && (
        <Alert severity="error" sx={{ mb: 3 }}>綜合分析失敗，請檢查股票代碼是否正確</Alert>
      )}

      {/* Stock analysis results */}
      {searchResults && <StockAnalysisResult data={searchResults} />}
      {analysisResults && <ComprehensiveAnalysisResult data={analysisResults} />}

      {/* Market Summary Tab Content */}
      {analysisTab === TAB_MARKET_SUMMARY && !isAuthenticated && (
        <Alert severity="info" sx={{ mb: 3 }} action={
          <Button color="inherit" size="small" onClick={() => { window.location.href = '/api/account/login/Google'; }}>登入</Button>
        }>
          今日市場AI摘要需要登入才能使用（會消耗 AI token）
        </Alert>
      )}
      {analysisTab === TAB_MARKET_SUMMARY && isAuthenticated && (
        <>
          {(isMarketLoading || isMarketFetching) && (
            <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', p: 5 }}>
              <CircularProgress />
              <Typography variant="h6" sx={{ ml: 2 }}>
                {isMarketFetching && !isMarketLoading ? '正在重新載入市場數據...' : '正在獲取最新市場數據...'}
              </Typography>
            </Box>
          )}
          {isMarketError && !isMarketFetching && (
            <Alert severity="error" sx={{ mb: 3 }} action={
              <IconButton color="inherit" size="small" onClick={() => refetch()} disabled={isMarketFetching}><RefreshIcon /></IconButton>
            }>
              <Typography variant="body1">無法載入市場數據，請點擊重新整理按鈕再試一次</Typography>
              {marketError && 'status' in marketError && (
                <Typography variant="caption" color="text.secondary">錯誤詳情: {marketError.status}</Typography>
              )}
            </Alert>
          )}
          {!isMarketLoading && !isMarketError && filteredData.length === 0 && (
            <Alert severity="info" sx={{ mb: 3 }}>沒有找到符合搜尋條件的公司。</Alert>
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
