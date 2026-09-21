import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import type { AnalysisConfig } from '../components';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import {
  useGetMarketDigestQuery,
  useAnalyzeStockMutation,
  useGetComprehensiveAnalysisMutation,
} from '@/apis/financeApis';
import { StockTrendAnalysis, StockAnalysisResultData } from '@/apis/financeApis/types';
import { todayISO } from '@/utils/date';

/** Detect market from stock code: numeric (with optional trailing letter) = TW, otherwise US */
const detectMarket = (code: string): 'TW' | 'US' =>
  /^\d{4,6}[A-Za-z]?$/.test(code.trim()) ? 'TW' : 'US';

// Tab indices
export const TAB_QUANTITATIVE = 0;
export const TAB_AI_TREND = 1;
export const TAB_MARKET_SUMMARY = 2;

const DEFAULT_CONFIG: AnalysisConfig = {
  includeTechnical: true,
  includeChip: true,
  includeFundamental: true,
  includeScoring: true,
  includeRisk: true,
  technicalWeight: 0.2,
  chipWeight: 0.3,
  fundamentalWeight: 0.5,
};

/**
 * Owns all Finance page state: search terms, stock/comprehensive analysis,
 * the active tab, analysis config, and the daily market-summary query.
 */
export function useFinanceAnalysis() {
  const [searchParams] = useSearchParams();
  const [stockSearchTerm, setStockSearchTerm] = useState('');
  const [searchResults, setSearchResults] = useState<StockTrendAnalysis | null>(null);
  const [analysisResults, setAnalysisResults] = useState<StockAnalysisResultData | null>(null);
  const [analysisTab, setAnalysisTab] = useState<number>(TAB_QUANTITATIVE);
  const [analysisConfig, setAnalysisConfig] = useState<AnalysisConfig>(DEFAULT_CONFIG);

  const currentDate = useMemo(() => todayISO(), []);
  const { data: userInfo } = useGetUserInfoQuery();
  const isAuthenticated = !!userInfo?.data;

  const {
    data: digestResponse,
    isLoading: isMarketLoading,
    isError: isMarketError,
    error: marketError,
    refetch,
    isFetching: isMarketFetching,
  } = useGetMarketDigestQuery(undefined, {
    // Only fetch when the digest tab is active AND user is authenticated (charges credits).
    skip: analysisTab !== TAB_MARKET_SUMMARY || !isAuthenticated,
  });

  const [analyzeStock, { isLoading: isAnalyzing, error: analysisError, reset: resetAnalysis }] =
    useAnalyzeStockMutation();

  const [
    getComprehensiveAnalysis,
    { isLoading: isComprehensiveLoading, error: comprehensiveError, reset: resetComprehensive },
  ] = useGetComprehensiveAnalysisMutation();

  const digestPoints = useMemo(() => digestResponse?.data?.points || [], [digestResponse?.data]);

  const runQuantitativeSearch = async (code: string, marketOverride?: 'TW' | 'US') => {
    try {
      resetComprehensive();
      setSearchResults(null);
      const market = marketOverride ?? detectMarket(code);
      const result = await getComprehensiveAnalysis({
        stockCode: code,
        market,
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
  };

  const handleStockSearch = async () => {
    if (!stockSearchTerm.trim()) return;

    if (analysisTab === TAB_QUANTITATIVE) {
      await runQuantitativeSearch(stockSearchTerm.trim());
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

  // Cross-link from a Portfolio holding (see HoldingDetailDrawer): pre-fill and auto-run the
  // free quantitative analysis for a symbol passed via the URL, exactly once on mount. Never
  // land on TAB_AI_TREND, which is credit-gated.
  useEffect(() => {
    const symbol = searchParams.get('symbol')?.trim();
    if (!symbol) return;
    const marketParam = searchParams.get('market');
    const market =
      marketParam === 'TW' || marketParam === 'US' ? marketParam : detectMarket(symbol);
    setStockSearchTerm(symbol);
    setAnalysisTab(TAB_QUANTITATIVE);
    void runQuantitativeSearch(symbol, market);
    // Intentionally mount-only: re-firing on every render/tab change would re-trigger the
    // search after the user has moved on to a different symbol or tab.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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

  return {
    // search / config state
    stockSearchTerm,
    setStockSearchTerm,
    analysisConfig,
    setAnalysisConfig,
    // results
    searchResults,
    analysisResults,
    // tab
    analysisTab,
    handleTabChange,
    // auth / misc
    isAuthenticated,
    currentDate,
    // market digest query
    digestPoints,
    isMarketLoading,
    isMarketError,
    marketError,
    isMarketFetching,
    refetch,
    // analysis status
    analysisError,
    isAnalyzing,
    comprehensiveError,
    isComprehensiveLoading,
    // actions / derived
    handleStockSearch,
    clearSearchResults,
    hasStockResults,
    isAnyAnalyzing,
    isSearchTab,
  };
}
