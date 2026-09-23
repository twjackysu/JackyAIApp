import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { addComparisonCode as addComparisonCodeToList } from '../utils/financeHelpers';

import type { AnalysisConfig } from '../components';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import {
  useGetMarketDigestQuery,
  useAnalyzeStockMutation,
  useGetComprehensiveAnalysisMutation,
} from '@/apis/financeApis';
import {
  StockTrendAnalysis,
  StockAnalysisResultData,
  StockAnalysisRequest,
} from '@/apis/financeApis/types';
import { getCaughtErrorMessage } from '@/apis/utils/errorMessage';
import { todayISO } from '@/utils/date';

type MarketCode = 'TW' | 'US';

/** Detect market from stock code: numeric (with optional trailing letter) = TW, otherwise US */
const detectMarket = (code: string): MarketCode =>
  /^\d{4,6}[A-Za-z]?$/.test(code.trim()) ? 'TW' : 'US';

// Tab indices
export const TAB_QUANTITATIVE = 0;
export const TAB_AI_TREND = 1;
export const TAB_MARKET_SUMMARY = 2;
export const TAB_COMPARISON = 3;

/** One entered stock's outcome in the comparison tab — either a result or a per-slot error. */
export interface ComparisonSlot {
  code: string;
  market: MarketCode;
  data: StockAnalysisResultData | null;
  error: string | null;
}

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
  const [analysisTab, setAnalysisTab] = useState<number>(TAB_QUANTITATIVE);
  const [analysisConfig, setAnalysisConfig] = useState<AnalysisConfig>(DEFAULT_CONFIG);
  const [comparisonCodes, setComparisonCodes] = useState<string[]>([]);
  const [comparisonInput, setComparisonInput] = useState('');
  const [comparisonSlots, setComparisonSlots] = useState<ComparisonSlot[] | null>(null);
  const [isComparing, setIsComparing] = useState(false);
  const [comparisonError, setComparisonError] = useState<string | null>(null);

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
  // Separate mutation-hook instance (same endpoint) for the comparison tab, so N concurrent
  // comparison calls never touch `isComprehensiveLoading`/`comprehensiveError` above — those
  // stay exactly what the single-stock quantitative tab already relied on.
  const [getComparisonAnalysis] = useGetComprehensiveAnalysisMutation();

  const digestPoints = useMemo(() => digestResponse?.data?.points || [], [digestResponse?.data]);

  // Shared request shape for both the single-stock tab and the comparison tab — same endpoint,
  // called once per stock either way.
  const buildAnalysisRequest = useCallback(
    (code: string, market: MarketCode): StockAnalysisRequest => ({
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
    }),
    [analysisConfig],
  );

  // Shared by the manual search button and the URL-driven auto-search below, so there's a
  // single call site for `getComprehensiveAnalysis`.
  const runComprehensiveAnalysis = useCallback(
    async (code: string, market: MarketCode) => {
      try {
        resetComprehensive();
        setSearchResults(null);
        const result = await getComprehensiveAnalysis(buildAnalysisRequest(code, market)).unwrap();
        setAnalysisResults(result.data);
      } catch (err) {
        console.error('Comprehensive analysis failed:', err);
        setAnalysisResults(null);
      }
    },
    [buildAnalysisRequest, getComprehensiveAnalysis, resetComprehensive],
  );

  const addComparisonCode = useCallback(() => {
    if (!comparisonInput.trim()) return;
    setComparisonCodes((prev) => addComparisonCodeToList(prev, comparisonInput));
    setComparisonInput('');
  }, [comparisonInput]);

  const removeComparisonCode = useCallback((code: string) => {
    setComparisonCodes((prev) => prev.filter((c) => c !== code));
  }, []);

  const fetchComparisonSlot = useCallback(
    async (code: string): Promise<ComparisonSlot> => {
      const market = detectMarket(code);
      try {
        const result = await getComparisonAnalysis(buildAnalysisRequest(code, market)).unwrap();
        return { code, market, data: result.data, error: null };
      } catch (err) {
        console.error(`Comparison fetch failed for ${code}:`, err);
        return { code, market, data: null, error: getCaughtErrorMessage(err) };
      }
    },
    [buildAnalysisRequest, getComparisonAnalysis],
  );

  // Fires one comprehensive-analysis call per entered code concurrently. Each call catches its
  // own failure (see fetchComparisonSlot) so one invalid code never blocks the others' results;
  // the try/catch here only guards against a truly unexpected failure outside those per-call
  // catches (e.g. building the request array itself throwing).
  const runComparison = useCallback(async () => {
    if (comparisonCodes.length === 0) return;
    setIsComparing(true);
    setComparisonError(null);
    try {
      const slots = await Promise.all(comparisonCodes.map(fetchComparisonSlot));
      setComparisonSlots(slots);
    } catch (err) {
      console.error('Comparison failed:', err);
      setComparisonSlots(null);
      setComparisonError(getCaughtErrorMessage(err));
    } finally {
      setIsComparing(false);
    }
  }, [comparisonCodes, fetchComparisonSlot]);

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
    setComparisonCodes([]);
    setComparisonInput('');
    setComparisonSlots(null);
    setComparisonError(null);
  };

  const handleTabChange = (_: unknown, newTab: number) => {
    setAnalysisTab(newTab);
    // Clear results when switching tabs
    clearSearchResults();
  };

  // Deep link from Portfolio's HoldingDetailDrawer: `?symbol=2330&market=TW` pre-fills the
  // search box, forces the free quantitative tab (never the credit-gated AI-trend tab), and
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
  const isAnyAnalyzing = isAnalyzing || isComprehensiveLoading || isComparing;
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
    // comparison tab
    comparisonCodes,
    comparisonInput,
    setComparisonInput,
    addComparisonCode,
    removeComparisonCode,
    comparisonSlots,
    isComparing,
    comparisonError,
    runComparison,
  };
}
