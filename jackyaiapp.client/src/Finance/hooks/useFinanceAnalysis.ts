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

type MarketCode = 'TW' | 'US';

/** Detect market from stock code: numeric (with optional trailing letter) = TW, otherwise US */
const detectMarket = (code: string): MarketCode =>
  /^\d{4,6}[A-Za-z]?$/.test(code.trim()) ? 'TW' : 'US';

// Tab indices, in display order. The assistant comes first and is what the page opens on.
export const TAB_AI_ASSISTANT = 0;
export const TAB_QUANTITATIVE = 1;
export const TAB_AI_TREND = 2;
export const TAB_MARKET_SUMMARY = 3;

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
  // Guards the one-time auto-search from a `?symbol=&market=` deep link (e.g. from Portfolio).
  const autoSearchFiredRef = useRef(false);
  const [stockSearchTerm, setStockSearchTerm] = useState('');
  const [searchResults, setSearchResults] = useState<StockTrendAnalysis | null>(null);
  const [analysisResults, setAnalysisResults] = useState<StockAnalysisResultData | null>(null);
  const [analysisTab, setAnalysisTab] = useState<number>(TAB_AI_ASSISTANT);
  const [analysisConfig, setAnalysisConfig] = useState<AnalysisConfig>(DEFAULT_CONFIG);

  const currentDate = useMemo(() => todayISO(), []);
  const { data: userInfo, isLoading: isAuthLoading } = useGetUserInfoQuery();
  const isAuthenticated = !!userInfo?.data;

  const {
    data: digestResponse,
    isLoading: isMarketLoading,
    isError: isMarketError,
    error: marketError,
    refetch,
    isFetching: isMarketFetching,
  } = useGetMarketDigestQuery(undefined, {
    // Only fetch when the digest tab is active AND user is authenticated (charges usage quota).
    skip: analysisTab !== TAB_MARKET_SUMMARY || !isAuthenticated,
  });

  const [analyzeStock, { isLoading: isAnalyzing, error: analysisError, reset: resetAnalysis }] =
    useAnalyzeStockMutation();

  const [
    getComprehensiveAnalysis,
    { isLoading: isComprehensiveLoading, error: comprehensiveError, reset: resetComprehensive },
  ] = useGetComprehensiveAnalysisMutation();

  const digestPoints = useMemo(() => digestResponse?.data?.points || [], [digestResponse?.data]);

  // Shared by the manual search button and the URL-driven auto-search below.
  const runComprehensiveAnalysis = useCallback(
    async (code: string, market: MarketCode) => {
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

  const handleStockSearch = async () => {
    if (!stockSearchTerm.trim()) return;

    if (analysisTab === TAB_QUANTITATIVE) {
      await runComprehensiveAnalysis(stockSearchTerm.trim(), detectMarket(stockSearchTerm.trim()));
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

  // Deep link from Portfolio's HoldingDetailDrawer: `?symbol=2330&market=TW` pre-fills the
  // search box, forces the free quantitative tab (never the usage-gated AI-trend tab), and
  // fires the search once. A plain `/finance` visit (no `symbol`) is unaffected.
  useEffect(() => {
    if (autoSearchFiredRef.current) return;
    const symbolParam = searchParams.get('symbol')?.trim();
    if (!symbolParam) return;
    autoSearchFiredRef.current = true;

    const marketParam = searchParams.get('market');
    const market: MarketCode =
      marketParam === 'TW' || marketParam === 'US' ? marketParam : detectMarket(symbolParam);

    setStockSearchTerm(symbolParam);
    setAnalysisTab(TAB_QUANTITATIVE);
    void runComprehensiveAnalysis(symbolParam, market);
  }, [searchParams, runComprehensiveAnalysis]);

  const hasStockResults = !!searchResults || !!analysisResults;
  const isAnyAnalyzing = isAnalyzing || isComprehensiveLoading;
  const isSearchTab = analysisTab === TAB_QUANTITATIVE || analysisTab === TAB_AI_TREND;
  // The comprehensive-analysis failure belongs to the quantitative tab; the AI-trend tab reports
  // its own (`analysisError`), so this one must not surface there.
  const showComprehensiveErrorAlert =
    analysisTab === TAB_QUANTITATIVE && !!comprehensiveError && !isComprehensiveLoading;

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
    // auth / misc — `isAuthenticated` is false until `isAuthLoading` is over, so a tab that
    // asks the visitor to sign in must wait for it or it flashes at every signed-in user
    isAuthenticated,
    isAuthLoading,
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
    showComprehensiveErrorAlert,
  };
}
