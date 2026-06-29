import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { ApiOkResponse } from '../types';
import {
  AddTransactionRequest,
  PortfolioSummaryDto,
  TransactionDto,
} from './types';

export const portfolioApis = createApi({
  reducerPath: 'portfolioApis',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/portfolio' }),
  tagTypes: ['Transactions', 'Summary'],
  endpoints: (builder) => ({
    getPortfolioSummary: builder.query<ApiOkResponse<PortfolioSummaryDto>, void>({
      query: () => 'summary',
      providesTags: ['Summary'],
    }),
    getTransactions: builder.query<ApiOkResponse<TransactionDto[]>, void>({
      query: () => 'transactions',
      providesTags: ['Transactions'],
    }),
    addTransaction: builder.mutation<ApiOkResponse<TransactionDto>, AddTransactionRequest>({
      query: (body) => ({ url: 'transactions', method: 'POST', body }),
      invalidatesTags: ['Transactions', 'Summary'],
    }),
    deleteTransaction: builder.mutation<ApiOkResponse<boolean>, number>({
      query: (id) => ({ url: `transactions/${id}`, method: 'DELETE' }),
      invalidatesTags: ['Transactions', 'Summary'],
    }),
  }),
});

export const {
  useGetPortfolioSummaryQuery,
  useGetTransactionsQuery,
  useAddTransactionMutation,
  useDeleteTransactionMutation,
} = portfolioApis;
