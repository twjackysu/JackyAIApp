import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { addComparisonCode, removeComparisonCode } from '../utils/comparisonCodes';

import type { AnalysisConfig } from '../components';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import {
  useGetMarketDigestQuery,
  useAnalyzeStockMutation,
  useGetComprehensiveAnalysisMutation,
} from '@/apis/financeApis';
import { StockTrendAnalysis, StockAnalysisResultData } from '@/apis/financeApis/types';
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

/** One compared stock's fetch state, rendered as its own column by `ComparisonResult`. */
export interface ComparisonSlot {
  code: string;
  market: MarketCode;
  status: 'loading' | 'success' | 'error';
  data: StockAnalysisResultData | null;
  errorMessage: string | null;
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
  const [comparisonInput, setComparisonInput] = useState('');
  const [comparisonCodes, setComparisonCodes] = useState<string[]>([]);
  const [comparisonSlots, setComparisonSlots] = useState<ComparisonSlot[]>([]);
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

  const digestPoints = useMemo(() => digestResponse?.data?.points || [], [digestResponse?.data]);

  // Single call site for `getComprehensiveAnalysis`'s request shape, shared by the manual
  // search button, the URL-driven auto-search below, and the multi-stock comparison tab —
  // each passes its own `config` (the user-adjustable `analysisConfig` for the single-stock
  // tabs, the fixed `DEFAULT_CONFIG` for comparison, which stays unconfigurable — see
  // ARCHITECTURE.md "Multi-stock/sector peer comparison").
  const fetchComprehensiveAnalysis = useCallback(
    (code: string, market: MarketCode, config: AnalysisConfig) =>
      getComprehensiveAnalysis({
        stockCode: code,
        market,
        includeTechnical: config.includeTechnical,
        includeChip: config.includeChip,
        includeFundamental: config.includeFundamental,
        includeScoring: config.includeScoring,
        includeRisk: config.includeRisk,
        technicalWeight: config.technicalWeight,
        chipWeight: config.chipWeight,
        fundamentalWeight: config.fundamentalWeight,
      }).unwrap(),
    [getComprehensiveAnalysis],
  );

  const runComprehensiveAnalysis = useCallback(
    async (code: string, market: MarketCode) => {
      try {
        resetComprehensive();
        setSearchResults(null);
        const result = await fetchComprehensiveAnalysis(code, market, analysisConfig);
        setAnalysisResults(result.data);
      } catch (err) {
        console.error('Comprehensive analysis failed:', err);
        setAnalysisResults(null);
      }
    },
    [analysisConfig, fetchComprehensiveAnalysis, resetComprehensive],
  );

  const handleAddComparisonCode = useCallback(() => {
    setComparisonCodes((prev) => addComparisonCode(prev, comparisonInput));
    setComparisonInput('');
  }, [comparisonInput]);

  const handleRemoveComparisonCode = useCallback((code: string) => {
    setComparisonCodes((prev) => removeComparisonCode(prev, code));
  }, []);

  const runComparison = useCallback(async () => {
    if (comparisonCodes.length === 0) return;
    setIsComparing(true);
    setComparisonError(null);
    setComparisonSlots(
      comparisonCodes.map((code) => ({
        code,
        market: detectMarket(code),
        status: 'loading',
        data: null,
        errorMessage: null,
      })),
    );
    try {
      await Promise.all(
        comparisonCodes.map(async (code) => {
          const market = detectMarket(code);
          try {
            const result = await fetchComprehensiveAnalysis(code, market, DEFAULT_CONFIG);
            setComparisonSlots((prev) =>
              prev.map((slot) =>
                slot.code === code ? { ...slot, status: 'success', data: result.data } : slot,
              ),
            );
          } catch (err) {
            console.error(`Comparison fetch failed for ${code}:`, err);
            setComparisonSlots((prev) =>
              prev.map((slot) =>
                slot.code === code
                  ? { ...slot, status: 'error', errorMessage: getCaughtErrorMessage(err) }
                  : slot,
              ),
            );
          }
        }),
      );
    } catch (err) {
      // Each per-stock call already catches its own failure above; this only guards against
      // something breaking outside that (e.g. Promise.all itself throwing unexpectedly).
      console.error('Comparison failed unexpectedly:', err);
      setComparisonError(getCaughtErrorMessage(err));
    } finally {
      setIsComparing(false);
    }
  }, [comparisonCodes, fetchComprehensiveAnalysis]);

  const clearComparisonResults = () => {
    setComparisonInput('');
    setComparisonCodes([]);
    setComparisonSlots([]);
    setComparisonError(null);
  };

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
    clearComparisonResults();
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

  const hasStockResults = !!searchResults || !!analysisResults || comparisonSlots.length > 0;
  const isAnyAnalyzing = isAnalyzing || isComprehensiveLoading;
  const isSearchTab = analysisTab === TAB_QUANTITATIVE || analysisTab === TAB_AI_TREND;
  // `comprehensiveError`/`isComprehensiveLoading` come from the single `getComprehensiveAnalysis`
  // mutation trigger shared with the comparison tab's `runComparison()` — RTK Query only reflects
  // the *last*-triggered call's state, so a failing comparison fetch can leave this truthy while
  // the user is on the comparison tab (which already shows its own per-slot errors). Gate it to
  // the tab it actually belongs to so `Finance/index.tsx` never renders it there (see TASK-016).
  const showComprehensiveError =
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
    showComprehensiveError,
    // actions / derived
    handleStockSearch,
    clearSearchResults,
    hasStockResults,
    isAnyAnalyzing,
    isSearchTab,
    // multi-stock comparison tab
    comparisonInput,
    setComparisonInput,
    comparisonCodes,
    comparisonSlots,
    isComparing,
    comparisonError,
    handleAddComparisonCode,
    handleRemoveComparisonCode,
    runComparison,
  };
}
