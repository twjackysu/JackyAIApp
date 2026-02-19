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
import { StockTrendAnalysis, StockAnalysisResultData } from '@/apis/financeApis/types';

import {
  StockAnalysisResult,
  MarketSummaryCard,
  ComprehensiveAnalysisResult,
  AnalysisConfigPanel,
} from './components';
import type { AnalysisConfig } from './components';
import { getCurrentDate } from './utils/financeHelpers';

function Finance() {
  const [searchTerm, setSearchTerm] = useState('');
  const [stockSearchTerm, setStockSearchTerm] = useState('');
  const [searchResults, setSearchResults] = useState<StockTrendAnalysis | null>(null);
  const [analysisResults, setAnalysisResults] = useState<StockAnalysisResultData | null>(null);
  const [analysisTab, setAnalysisTab] = useState<number>(0);
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

    if (analysisTab === 0) {
      try {
        resetAnalysis();
        setAnalysisResults(null);
        const result = await analyzeStock({ stockCodeOrName: stockSearchTerm.trim() }).unwrap();
        setSearchResults(result.data);
      } catch (err) {
        console.error('Stock analysis failed:', err);
        setSearchResults(null);
      }
    } else {
      try {
        resetComprehensive();
        setSearchResults(null);
        const result = await getComprehensiveAnalysis({
          stockCode: stockSearchTerm.trim(),
          includeTechnical: analysisConfig.includeTechnical,
          includeChip: analysisConfig.includeChip,
          includeFundamental: analysisConfig.includeFundamental,
          includeScoring: analysisConfig.includeScoring,
          includeRisk: analysisConfig.includeRisk,
          technicalWeight: analysisConfig.technicalWeight,
          chipWeight: analysisConfig.chipWeight,
          fundamentalWeight: analysisConfig.fundamentalWeight,
        }).unwrap();
        setAnalysisResults(result.data);
      } catch (err) {
        console.error('Comprehensive analysis failed:', err);
        setAnalysisResults(null);
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

  const hasAnyResults = !!searchResults || !!analysisResults;
  const isAnyAnalyzing = isAnalyzing || isComprehensiveLoading;

  return (
    <Box sx={{ p: 3 }}>
      {/* Header */}
      <Stack direction="row" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4" component="h1" fontWeight="bold">
          {hasAnyResults ? '股票分析 (Stock Analysis)' : '今日市場摘要 (Market Summary)'}
        </Typography>
        <Stack direction="row" alignItems="center" spacing={2}>
          <Typography variant="subtitle1" color="text.secondary">{currentDate}</Typography>
          {!hasAnyResults && (
            <IconButton onClick={() => refetch()} disabled={isFetching} size="small" sx={{ bgcolor: 'action.hover', '&:hover': { bgcolor: 'action.selected' } }}>
              <RefreshIcon />
            </IconButton>
          )}
        </Stack>
      </Stack>

      {/* Search Section with Tabs */}
      <Paper sx={{ p: 3, mb: 3, border: '1px solid', borderColor: 'divider' }}>
        <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold', color: 'primary.main' }}>
          股票分析搜尋 (Stock Analysis)
        </Typography>
        <Tabs value={analysisTab} onChange={(_, v) => setAnalysisTab(v)} sx={{ mb: 2 }}>
          <Tab label="🤖 AI 趨勢分析" />
          <Tab label="📊 綜合量化分析" />
        </Tabs>
        <Stack direction="row" spacing={2} alignItems="center">
          <TextField
            fullWidth variant="outlined"
            placeholder="輸入股票代碼或公司名稱 (例如: 2330, 台積電)"
            value={stockSearchTerm}
            onChange={(e) => setStockSearchTerm(e.target.value)}
            onKeyPress={(e) => e.key === 'Enter' && handleStockSearch()}
            disabled={isAnyAnalyzing}
            InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> }}
            sx={{ flexGrow: 1 }}
          />
          <Button variant="contained" onClick={handleStockSearch} disabled={isAnyAnalyzing || !stockSearchTerm.trim()} sx={{ minWidth: 120, height: 56 }}>
            {isAnyAnalyzing ? <CircularProgress size={20} color="inherit" /> : '分析'}
          </Button>
          {hasAnyResults && (
            <Button variant="outlined" onClick={clearSearchResults} disabled={isAnyAnalyzing} sx={{ height: 56 }}>清除</Button>
          )}
        </Stack>
        {isAnyAnalyzing && (
          <Box sx={{ mt: 2 }}>
            <LinearProgress />
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              {analysisTab === 0 ? '正在分析股票資料並產生趨勢預測...這可能需要 1-2 分鐘' : '正在計算技術指標與籌碼分析...'}
            </Typography>
          </Box>
        )}
        {analysisTab === 1 && (
          <Box sx={{ mt: 2 }}><AnalysisConfigPanel config={analysisConfig} onChange={setAnalysisConfig} /></Box>
        )}
      </Paper>

      {/* Market Summary Filter */}
      {!hasAnyResults && (
        <Paper sx={{ p: 2, mb: 3 }}>
          <TextField fullWidth variant="outlined" placeholder="搜尋今日市場摘要..." value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)}
            InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> }} sx={{ mb: 1 }} />
        </Paper>
      )}

      {/* Loading / Error States */}
      {!hasAnyResults && (isLoading || isFetching) && (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 5 }}>
          <CircularProgress /><Typography variant="h6" sx={{ ml: 2 }}>{isFetching && !isLoading ? '正在重新載入市場數據...' : '正在獲取最新市場數據...'}</Typography>
        </Box>
      )}
      {!hasAnyResults && isError && !isFetching && (
        <Alert severity="error" sx={{ mb: 3 }} action={<IconButton color="inherit" size="small" onClick={() => refetch()} disabled={isFetching}><RefreshIcon /></IconButton>}>
          <Typography variant="body1">無法載入市場數據，請點擊重新整理按鈕再試一次</Typography>
          {error && 'status' in error && <Typography variant="caption" color="text.secondary">錯誤詳情: {error.status}</Typography>}
        </Alert>
      )}
      {analysisError && !isAnalyzing && (
        <Alert severity="error" sx={{ mb: 3 }}>股票分析失敗，請檢查股票代碼是否正確</Alert>
      )}
      {comprehensiveError && !isComprehensiveLoading && (
        <Alert severity="error" sx={{ mb: 3 }}>綜合分析失敗，請檢查股票代碼是否正確</Alert>
      )}

      {/* Results */}
      {searchResults && <StockAnalysisResult data={searchResults} />}
      {analysisResults && <ComprehensiveAnalysisResult data={analysisResults} />}

      {/* Market Summary Cards */}
      {!hasAnyResults && (
        <>
          {!isLoading && !isError && filteredData.length === 0 && <Alert severity="info" sx={{ mb: 3 }}>沒有找到符合搜尋條件的公司。</Alert>}
          <Grid container spacing={3}>
            {filteredData.map((stock) => (
              <Grid item xs={12} sm={6} md={4} key={stock.stockCode}><MarketSummaryCard stock={stock} /></Grid>
            ))}
          </Grid>
        </>
      )}
    </Box>
  );
}

export default Finance;
