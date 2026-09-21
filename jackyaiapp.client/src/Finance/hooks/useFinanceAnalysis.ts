import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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

  const runQuantitativeAnalysis = useCallback(
    async (code: string, market: 'TW' | 'US') => {
      try {
        resetComprehensive();
        setSearchResults(null);
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
    },
    [analysisConfig, getComprehensiveAnalysis, resetComprehensive],
  );

  // Auto-run the quantitative (free) analysis once when the URL carries a symbol — e.g. a
  // cross-link from a Portfolio holding. Never fires again after the initial mount, so it
  // doesn't re-trigger once the user has moved on to their own search/tab.
  const autoSearchFiredRef = useRef(false);
  useEffect(() => {
    if (autoSearchFiredRef.current) return;
    const urlSymbol = searchParams.get('symbol')?.trim();
    if (!urlSymbol) return;
    autoSearchFiredRef.current = true;

    const urlMarket = searchParams.get('market');
    const market = urlMarket === 'TW' || urlMarket === 'US' ? urlMarket : detectMarket(urlSymbol);

    setStockSearchTerm(urlSymbol);
    setAnalysisTab(TAB_QUANTITATIVE);
    void runQuantitativeAnalysis(urlSymbol, market);
  }, [searchParams, runQuantitativeAnalysis]);

  const handleStockSearch = async () => {
    if (!stockSearchTerm.trim()) return;

    if (analysisTab === TAB_QUANTITATIVE) {
      const code = stockSearchTerm.trim();
      await runQuantitativeAnalysis(code, detectMarket(code));
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
