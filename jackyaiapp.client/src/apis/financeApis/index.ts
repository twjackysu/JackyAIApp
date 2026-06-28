// filepath: e:\Users\jacky\Documents\Repo\JackyAIApp\jackyaiapp.client\src\apis\financeApis\index.ts

import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';

import {
  StrategicInsight,
  StockTrendAnalysis,
  StockSearchRequest,
  StockAnalysisResultData,
  StockAnalysisRequest,
  MacroEconomyData,
  MarketOverviewData,
  MarketBriefData,
  IndicatorExplorerData,
  IndicatorInsightData,
} from './types';

// Define a service using a base URL and expected endpoints
export const financeApis = createApi({
  reducerPath: 'financeApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/finance',
  }),
  tagTypes: ['DailyInfo', 'StockAnalysis'],
  endpoints: (builder) => ({
    getDailyImportantInfo: builder.query<ApiOkResponse<StrategicInsight[]>, void>({
      query: () => ({
        url: `dailyimportantinfo`,
      }),
      providesTags: ['DailyInfo'],
      // Force refetch on errors by not caching failed requests
      keepUnusedDataFor: 0, // Don't cache failed requests
      // Retry failed requests
      transformErrorResponse: (response) => {
        // Log error for debugging
        console.error('Finance API error:', response);
        return response;
      },
    }),
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
    getMarketBrief: builder.query<ApiOkResponse<MarketBriefData>, void>({
      query: () => ({ url: 'market-brief' }),
      keepUnusedDataFor: 600,
    }),
    getIndicators: builder.query<ApiOkResponse<IndicatorExplorerData>, void>({
      query: () => ({ url: 'indicators' }),
      keepUnusedDataFor: 3600,
    }),
    getIndicatorInsight: builder.query<ApiOkResponse<IndicatorInsightData>, string>({
      query: (id) => ({ url: `indicators/${id}/insight` }),
      keepUnusedDataFor: 3600,
    }),
  }),
});

export const {
  useGetDailyImportantInfoQuery,
  useAnalyzeStockMutation,
  useGetComprehensiveAnalysisMutation,
  useGetMacroEconomyQuery,
  useGetMarketOverviewQuery,
  useGetMarketBriefQuery,
  useLazyGetMarketBriefQuery,
  useGetIndicatorsQuery,
  useLazyGetIndicatorInsightQuery,
} = financeApis;
