import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';

import {
  HoldingPriceHistoryDto,
  NetWorthDataPoint,
  PortfolioSummaryDto,
  SaveHoldingsRequest,
  StockLookupDto,
  UpdateHistoryItem,
} from './types';

export const portfolioApis = createApi({
  reducerPath: 'portfolioApis',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/portfolio' }),
  tagTypes: ['Holdings', 'Summary'],
  endpoints: (builder) => ({
    getPortfolioSummary: builder.query<ApiOkResponse<PortfolioSummaryDto>, void>({
      query: () => 'summary',
      providesTags: ['Summary'],
    }),
    getHoldings: builder.query<ApiOkResponse<SaveHoldingsRequest>, void>({
      query: () => 'holdings',
      providesTags: ['Holdings'],
    }),
    saveHoldings: builder.mutation<ApiOkResponse<boolean>, SaveHoldingsRequest>({
      query: (body) => ({ url: 'holdings', method: 'PUT', body }),
      invalidatesTags: ['Holdings', 'Summary'],
    }),
    getUpdateHistory: builder.query<ApiOkResponse<UpdateHistoryItem[]>, void>({
      query: () => 'update-history',
      providesTags: ['Holdings'],
    }),
    getNetWorthHistory: builder.query<ApiOkResponse<NetWorthDataPoint[]>, number>({
      query: (days) => `net-worth-history?days=${days}`,
      providesTags: ['Summary'],
    }),
    lookupStock: builder.query<ApiOkResponse<StockLookupDto>, { market: string; symbol: string }>({
      query: ({ market, symbol }) => `lookup/${market}/${symbol}`,
    }),
    getHoldingPriceHistory: builder.query<
      ApiOkResponse<HoldingPriceHistoryDto>,
      { market: string; symbol: string }
    >({
      query: ({ market, symbol }) => `holdings/${market}/${symbol}/price-history`,
    }),
  }),
});

export const {
  useGetPortfolioSummaryQuery,
  useGetHoldingsQuery,
  useSaveHoldingsMutation,
  useGetUpdateHistoryQuery,
  useGetNetWorthHistoryQuery,
  useLookupStockQuery,
  useGetHoldingPriceHistoryQuery,
} = portfolioApis;
