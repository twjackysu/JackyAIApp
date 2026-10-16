// filepath: e:\Users\jacky\Documents\Repo\JackyAIApp\jackyaiapp.client\src\apis\financeApis\index.ts

import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import {
  StockTrendAnalysis,
  StockSearchRequest,
  StockAnalysisResultData,
  StockAnalysisRequest,
  MacroEconomyData,
  MarketOverviewData,
  MarketDigestData,
  IndicatorExplorerData,
  IndicatorInsightData,
  MarketIndexHistoryData,
  PriceBarsData,
  PriceBarsRequest,
} from './types';

// Define a service using a base URL and expected endpoints
export const financeApis = createApi({
  reducerPath: 'financeApis',
  baseQuery: apiBaseQuery('/api/finance'),
  tagTypes: ['StockAnalysis'],
  endpoints: (builder) => ({
    analyzeStock: builder.mutation<ApiOkResponse<StockTrendAnalysis>, StockSearchRequest>({
      query: (request) => ({
        url: 'analyze-stock',
        method: 'POST',
        body: request,
      }),
      invalidatesTags: ['StockAnalysis'],
      transformErrorResponse: (response) => {
        console.error('Stock analysis error:', response);
        return response;
      },
    }),
    getComprehensiveAnalysis: builder.mutation<
      ApiOkResponse<StockAnalysisResultData>,
      StockAnalysisRequest
    >({
      query: (request) => ({
        url: 'comprehensive-analysis',
        method: 'POST',
        body: request,
      }),
      transformErrorResponse: (response) => {
        console.error('Comprehensive analysis error:', response);
        return response;
      },
    }),
    getMacroEconomy: builder.query<ApiOkResponse<MacroEconomyData>, void>({
      query: () => ({ url: 'macro-economy' }),
      keepUnusedDataFor: 3600, // cache 1 hour client-side
    }),
    getMarketOverview: builder.query<ApiOkResponse<MarketOverviewData>, void>({
      query: () => ({ url: 'market-overview' }),
      keepUnusedDataFor: 180, // intraday snapshot; refresh ~3 min
    }),
    getMarketDigest: builder.query<ApiOkResponse<MarketDigestData>, void>({
      query: () => ({ url: 'market-digest' }),
      // Day-stable content; keep it cached so revisiting the tab within a session
      // doesn't re-hit the server (and the per-day usage charge stays a once-a-day thing).
      keepUnusedDataFor: 3600,
    }),
    getIndicators: builder.query<ApiOkResponse<IndicatorExplorerData>, void>({
      query: () => ({ url: 'indicators' }),
      keepUnusedDataFor: 3600,
    }),
    getMarketIndexHistory: builder.query<ApiOkResponse<MarketIndexHistoryData>, void>({
      query: () => ({ url: 'market-index-history' }),
      // Shared, server-cached daily closes (1 h server TTL); switching the chart's date range is
      // pure client-side math, so the same payload serves every range the user tries.
      keepUnusedDataFor: 3600,
    }),
    getIndicatorInsight: builder.query<ApiOkResponse<IndicatorInsightData>, string>({
      query: (id) => ({ url: `indicators/${id}/insight` }),
      keepUnusedDataFor: 3600,
    }),
    /** Daily OHLC bars for the trendline chart (the lines are computed client-side). */
    getPriceBars: builder.query<ApiOkResponse<PriceBarsData>, PriceBarsRequest>({
      query: ({ code, market }) => ({
        url: 'price-bars',
        params: market ? { code, market } : { code },
      }),
      keepUnusedDataFor: 3600,
    }),
  }),
});

export const {
  useAnalyzeStockMutation,
  useGetComprehensiveAnalysisMutation,
  useGetMacroEconomyQuery,
  useGetMarketOverviewQuery,
  useGetMarketDigestQuery,
  useGetIndicatorsQuery,
  useGetMarketIndexHistoryQuery,
  useLazyGetIndicatorInsightQuery,
  useGetPriceBarsQuery,
} = financeApis;
