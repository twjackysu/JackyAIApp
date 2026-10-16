import ArticleIcon from '@mui/icons-material/Article';
import AssessmentIcon from '@mui/icons-material/Assessment';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import CompareArrowsIcon from '@mui/icons-material/CompareArrows';
import InsightsIcon from '@mui/icons-material/Insights';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import LinearProgress from '@mui/material/LinearProgress';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import {
  StockAnalysisResult,
  ComprehensiveAnalysisResult,
  AnalysisConfigPanel,
  MarketOverview,
  IndicatorExplorer,
  MarketSeasonalChart,
  TaiexTrendlineCard,
} from './components';
import { ComparisonCodeInput } from './components/comparison/ComparisonCodeInput';
import { ComparisonResult } from './components/comparison/ComparisonResult';
import { MarketSummaryTab } from './components/MarketSummaryTab';
import {
  useFinanceAnalysis,
  TAB_QUANTITATIVE,
  TAB_AI_TREND,
  TAB_MARKET_SUMMARY,
  TAB_COMPARISON,
} from './hooks/useFinanceAnalysis';

function Finance() {
  const {
    stockSearchTerm,
    setStockSearchTerm,
    analysisConfig,
    setAnalysisConfig,
    searchResults,
    analysisResults,
    analysisTab,
    handleTabChange,
    isAuthenticated,
    currentDate,
    digestPoints,
    isMarketLoading,
    isMarketError,
    marketError,
    isMarketFetching,
    refetch,
    analysisError,
    isAnalyzing,
    showComprehensiveErrorAlert,
    handleStockSearch,
    clearSearchResults,
    hasStockResults,
    isAnyAnalyzing,
    isSearchTab,
    comparisonInput,
    setComparisonInput,
    comparisonCodes,
    comparisonSlots,
    isComparing,
    comparisonError,
    handleAddComparisonCode,
    handleRemoveComparisonCode,
    runComparison,
  } = useFinanceAnalysis();

  return (
    <Box sx={{ p: { xs: 1.5, sm: 3 } }}>
      {/* Header */}
      <Stack
        direction="row"
        sx={{
          justifyContent: 'space-between',
          alignItems: 'center',
          mb: 2,
        }}
      >
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', minWidth: 0 }}>
          <InsightsIcon sx={{ fontSize: { xs: '1.4rem', sm: '1.7rem' } }} />
          <Typography
            variant="h5"
            component="h1"
            sx={{
              fontWeight: 'bold',
              fontSize: { xs: '1.2rem', sm: '1.5rem' },
            }}
          >
            Stock Intelligence
          </Typography>
        </Stack>
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ display: { xs: 'none', sm: 'block' } }}
        >
          {currentDate}
        </Typography>
      </Stack>
      {!hasStockResults && (
        <>
          <MarketOverview />
          <IndicatorExplorer />
          <MarketSeasonalChart />
          <TaiexTrendlineCard />
        </>
      )}
      {/* Tabs */}
      <Paper sx={{ p: { xs: 1.5, sm: 3 }, mb: 2, border: '1px solid', borderColor: 'divider' }}>
        <Tabs
          value={analysisTab}
          onChange={handleTabChange}
          variant="scrollable"
          scrollButtons="auto"
          allowScrollButtonsMobile
          sx={{ mb: 2 }}
        >
          <Tab icon={<AssessmentIcon />} iconPosition="start" label="綜合量化分析" />
          <Tab icon={<AutoAwesomeIcon />} iconPosition="start" label="AI 趨勢分析" />
          <Tab icon={<ArticleIcon />} iconPosition="start" label="今日市場AI摘要" />
          <Tab icon={<CompareArrowsIcon />} iconPosition="start" label="多股比較" />
        </Tabs>

        {/* Login prompt for AI tabs */}
        {!isAuthenticated && analysisTab === TAB_AI_TREND && (
          <Alert
            severity="info"
            sx={{ mb: 2 }}
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
            AI 趨勢分析需要登入才能使用（會消耗 AI token）
          </Alert>
        )}

        {/* Search bar for stock analysis tabs */}
        {isSearchTab && (
          <>
            <Stack
              direction="row"
              spacing={1}
              sx={{
                alignItems: 'center',
              }}
            >
              <TextField
                fullWidth
                variant="outlined"
                size="small"
                placeholder="股票代碼 (2330, AAPL...)"
                value={stockSearchTerm}
                onChange={(e) => setStockSearchTerm(e.target.value)}
                onKeyPress={(e) => e.key === 'Enter' && handleStockSearch()}
                disabled={isAnyAnalyzing || (analysisTab === TAB_AI_TREND && !isAuthenticated)}
                sx={{ flexGrow: 1 }}
                slotProps={{
                  input: {
                    startAdornment: (
                      <InputAdornment position="start">
                        <SearchIcon />
                      </InputAdornment>
                    ),
                    endAdornment: hasStockResults ? (
                      <InputAdornment position="end">
                        <IconButton
                          size="small"
                          onClick={clearSearchResults}
                          disabled={isAnyAnalyzing}
                          edge="end"
                        >
                          <RefreshIcon fontSize="small" />
                        </IconButton>
                      </InputAdornment>
                    ) : undefined,
                  },
                }}
              />
              <Button
                variant="contained"
                onClick={handleStockSearch}
                disabled={
                  isAnyAnalyzing ||
                  !stockSearchTerm.trim() ||
                  (analysisTab === TAB_AI_TREND && !isAuthenticated)
                }
                sx={{ minWidth: { xs: 56, sm: 100 }, height: 40, px: { xs: 1, sm: 3 } }}
              >
                {isAnyAnalyzing ? (
                  <CircularProgress size={18} color="inherit" />
                ) : (
                  <>
                    {
                      <Box component="span" sx={{ display: { xs: 'none', sm: 'inline' } }}>
                        分析
                      </Box>
                    }
                    <Box component="span" sx={{ display: { xs: 'inline', sm: 'none' } }}>
                      <SearchIcon fontSize="small" />
                    </Box>
                  </>
                )}
              </Button>
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
              <Box sx={{ mt: 2 }}>
                <AnalysisConfigPanel config={analysisConfig} onChange={setAnalysisConfig} />
              </Box>
            )}
          </>
        )}

        {/* Multi-code input for the comparison tab */}
        {analysisTab === TAB_COMPARISON && (
          <>
            <ComparisonCodeInput
              codes={comparisonCodes}
              inputValue={comparisonInput}
              onInputChange={setComparisonInput}
              onAdd={handleAddComparisonCode}
              onRemove={handleRemoveComparisonCode}
              onCompare={runComparison}
              isComparing={isComparing}
            />
            {isComparing && (
              <Box sx={{ mt: 2 }}>
                <LinearProgress />
                <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                  正在比較 {comparisonCodes.length} 檔股票的量化分析...
                </Typography>
              </Box>
            )}
          </>
        )}
      </Paper>
      {/* Error states */}
      {analysisError && !isAnalyzing && (
        <Alert severity="error" sx={{ mb: 3 }}>
          股票分析失敗，請檢查股票代碼是否正確
        </Alert>
      )}
      {showComprehensiveErrorAlert && (
        <Alert severity="error" sx={{ mb: 3 }}>
          綜合分析失敗，請檢查股票代碼是否正確
        </Alert>
      )}
      {comparisonError && !isComparing && (
        <Alert severity="error" sx={{ mb: 3 }}>
          {comparisonError}
        </Alert>
      )}
      {/* Stock analysis results */}
      {searchResults && <StockAnalysisResult data={searchResults} />}
      {analysisResults && <ComprehensiveAnalysisResult data={analysisResults} />}
      {/* Comparison results */}
      {analysisTab === TAB_COMPARISON && comparisonSlots.length > 0 && (
        <ComparisonResult slots={comparisonSlots} />
      )}
      {/* Market Summary Tab Content */}
      {analysisTab === TAB_MARKET_SUMMARY && (
        <MarketSummaryTab
          isAuthenticated={isAuthenticated}
          isMarketLoading={isMarketLoading}
          isMarketFetching={isMarketFetching}
          isMarketError={isMarketError}
          marketError={marketError}
          digestPoints={digestPoints}
          refetch={refetch}
        />
      )}
    </Box>
  );
}

export default Finance;
