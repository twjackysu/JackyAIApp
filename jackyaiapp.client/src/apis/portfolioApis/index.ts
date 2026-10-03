import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import {
  BenchmarkComparison,
  DrawdownAnalysis,
  FundSearchItem,
  HoldingPriceHistoryDto,
  NetWorthDataPoint,
  NetWorthGroupBy,
  PortfolioAiSummaryDto,
  PortfolioSummaryDto,
  SaveHoldingsRequest,
  StockLookupDto,
  StockSearchItem,
  UpdateHistoryItem,
} from './types';

export const portfolioApis = createApi({
  reducerPath: 'portfolioApis',
  baseQuery: apiBaseQuery('/api/portfolio'),
  tagTypes: ['Holdings', 'Summary'],
  endpoints: (builder) => ({
    // feeDiscountRate is a client-side preference (localStorage — see Portfolio/feeDiscountRate.ts),
    // not server-persisted, so it's passed as a query param on every summary fetch instead of a
    // separate settings endpoint.
    getPortfolioSummary: builder.query<ApiOkResponse<PortfolioSummaryDto>, number | void>({
      query: (feeDiscountRate) =>
        `summary${feeDiscountRate ? `?feeDiscountRate=${feeDiscountRate}` : ''}`,
      providesTags: ['Summary'],
    }),
    getHoldings: builder.query<ApiOkResponse<SaveHoldingsRequest>, void>({
      query: () => 'holdings',
      providesTags: ['Holdings'],
    }),
    getSnapshotHoldings: builder.query<ApiOkResponse<SaveHoldingsRequest>, number>({
      query: (snapshotId) => `holdings/${snapshotId}`,
    }),
    saveHoldings: builder.mutation<ApiOkResponse<boolean>, SaveHoldingsRequest>({
      query: (body) => ({ url: 'holdings', method: 'PUT', body }),
      invalidatesTags: ['Holdings', 'Summary'],
    }),
    deleteSnapshot: builder.mutation<ApiOkResponse<boolean>, number>({
      query: (snapshotId) => ({ url: `holdings/${snapshotId}`, method: 'DELETE' }),
      invalidatesTags: ['Holdings', 'Summary'],
    }),
    refreshPositionPrice: builder.mutation<
      ApiOkResponse<PortfolioSummaryDto>,
      { symbol: string; feeDiscountRate?: number }
    >({
      query: ({ symbol, feeDiscountRate }) => ({
        url: `holdings/${symbol}/refresh-price${feeDiscountRate ? `?feeDiscountRate=${feeDiscountRate}` : ''}`,
        method: 'POST',
      }),
      invalidatesTags: ['Summary'],
    }),
    getUpdateHistory: builder.query<ApiOkResponse<UpdateHistoryItem[]>, void>({
      query: () => 'update-history',
      providesTags: ['Holdings'],
    }),
    searchFunds: builder.query<ApiOkResponse<FundSearchItem[]>, string>({
      query: (keyword) => `funds/search?keyword=${encodeURIComponent(keyword)}`,
    }),
    getNetWorthHistory: builder.query<
      ApiOkResponse<NetWorthDataPoint[]>,
      // Either a preset span (`days`) or an explicit window (`from`/`to`, yyyy-MM-dd). The server
      // treats the presence of either date as the explicit-window mode.
      { days?: number; from?: string; to?: string; groupBy?: NetWorthGroupBy }
    >({
      query: ({ days, from, to, groupBy }) => {
        const params = new URLSearchParams();
        if (from || to) {
          if (from) params.set('from', from);
          if (to) params.set('to', to);
        } else if (days != null) {
          params.set('days', String(days));
        }
        if (groupBy) params.set('groupBy', groupBy);
        return `net-worth-history?${params}`;
      },
      providesTags: ['Summary'],
    }),
    getDrawdown: builder.query<ApiOkResponse<DrawdownAnalysis>, number>({
      query: (days) => `drawdown?days=${days}`,
      providesTags: ['Summary'],
    }),
    getBenchmarkComparison: builder.query<
      ApiOkResponse<BenchmarkComparison>,
      { days: number; symbols?: string[] }
    >({
      query: ({ days, symbols }) => {
        const symbolParams = (symbols ?? [])
          .map((s) => `&symbols=${encodeURIComponent(s)}`)
          .join('');
        return `benchmark-comparison?days=${days}${symbolParams}`;
      },
      providesTags: ['Summary'],
    }),
    lookupStock: builder.query<ApiOkResponse<StockLookupDto>, { market: string; symbol: string }>({
      query: ({ market, symbol }) => `lookup/${market}/${symbol}`,
    }),
    searchStocks: builder.query<
      ApiOkResponse<StockSearchItem[]>,
      { market: string; keyword: string }
    >({
      query: ({ market, keyword }) =>
        `stocks/search?market=${market}&keyword=${encodeURIComponent(keyword)}`,
    }),
    getHoldingPriceHistory: builder.query<
      ApiOkResponse<HoldingPriceHistoryDto>,
      { market: string; symbol: string }
    >({
      query: ({ market, symbol }) => `holdings/${market}/${symbol}/price-history`,
    }),
    getAiTechSummary: builder.query<
      ApiOkResponse<PortfolioAiSummaryDto>,
      { market: string; symbol: string }
    >({
      query: ({ market, symbol }) => `holdings/${market}/${symbol}/ai-summary`,
    }),
  }),
});

export const {
  useGetPortfolioSummaryQuery,
  useGetHoldingsQuery,
  useLazyGetSnapshotHoldingsQuery,
  useSaveHoldingsMutation,
  useDeleteSnapshotMutation,
  useRefreshPositionPriceMutation,
  useGetUpdateHistoryQuery,
  useSearchFundsQuery,
  useGetNetWorthHistoryQuery,
  useLazyGetNetWorthHistoryQuery,
  useGetDrawdownQuery,
  useGetBenchmarkComparisonQuery,
  useLookupStockQuery,
  useSearchStocksQuery,
  useGetHoldingPriceHistoryQuery,
  useLazyGetAiTechSummaryQuery,
} = portfolioApis;
