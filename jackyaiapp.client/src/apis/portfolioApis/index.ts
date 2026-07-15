import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import {
  FundSearchItem,
  HoldingPriceHistoryDto,
  NetWorthDataPoint,
  NetWorthGroupBy,
  PortfolioAiSummaryDto,
  PortfolioSettingsDto,
  PortfolioSummaryDto,
  SaveHoldingsRequest,
  StockLookupDto,
  StockSearchItem,
  UpdateHistoryItem,
} from './types';

export const portfolioApis = createApi({
  reducerPath: 'portfolioApis',
  baseQuery: apiBaseQuery('/api/portfolio'),
  tagTypes: ['Holdings', 'Summary', 'Settings'],
  endpoints: (builder) => ({
    getPortfolioSummary: builder.query<ApiOkResponse<PortfolioSummaryDto>, void>({
      query: () => 'summary',
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
    getUpdateHistory: builder.query<ApiOkResponse<UpdateHistoryItem[]>, void>({
      query: () => 'update-history',
      providesTags: ['Holdings'],
    }),
    searchFunds: builder.query<ApiOkResponse<FundSearchItem[]>, string>({
      query: (keyword) => `funds/search?keyword=${encodeURIComponent(keyword)}`,
    }),
    getNetWorthHistory: builder.query<
      ApiOkResponse<NetWorthDataPoint[]>,
      { days: number; groupBy?: NetWorthGroupBy }
    >({
      query: ({ days, groupBy }) =>
        `net-worth-history?days=${days}${groupBy ? `&groupBy=${groupBy}` : ''}`,
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
    getPortfolioSettings: builder.query<ApiOkResponse<PortfolioSettingsDto>, void>({
      query: () => 'settings',
      providesTags: ['Settings'],
    }),
    savePortfolioSettings: builder.mutation<ApiOkResponse<boolean>, PortfolioSettingsDto>({
      query: (body) => ({ url: 'settings', method: 'PUT', body }),
      // Settings feed directly into the summary's fee/tax deduction, so a saved rate must
      // invalidate the cached summary too, not just Settings itself.
      invalidatesTags: ['Settings', 'Summary'],
    }),
  }),
});

export const {
  useGetPortfolioSummaryQuery,
  useGetHoldingsQuery,
  useLazyGetSnapshotHoldingsQuery,
  useSaveHoldingsMutation,
  useDeleteSnapshotMutation,
  useGetUpdateHistoryQuery,
  useSearchFundsQuery,
  useGetNetWorthHistoryQuery,
  useLazyGetNetWorthHistoryQuery,
  useLookupStockQuery,
  useSearchStocksQuery,
  useGetHoldingPriceHistoryQuery,
  useLazyGetAiTechSummaryQuery,
  useGetPortfolioSettingsQuery,
  useSavePortfolioSettingsMutation,
} = portfolioApis;
