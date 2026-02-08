import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';

import {
  CreditBalanceResponse,
  CreditCheckResponse,
  CreditHistoryResponse,
  GetHistoryRequest,
} from './types';

export const creditApis = createApi({
  reducerPath: 'creditApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/credit',
  }),
  tagTypes: ['CreditBalance', 'CreditHistory'],
  endpoints: (builder) => ({
    getCreditBalance: builder.query<ApiOkResponse<CreditBalanceResponse>, void>({
      query: () => ({
        url: 'balance',
      }),
      providesTags: ['CreditBalance'],
    }),
    getCreditHistory: builder.query<ApiOkResponse<CreditHistoryResponse>, GetHistoryRequest>({
      query: ({ pageNumber = 1, pageSize = 20 } = {}) => ({
        url: `history?pageNumber=${pageNumber}&pageSize=${pageSize}`,
      }),
      providesTags: ['CreditHistory'],
    }),
    checkCredits: builder.query<ApiOkResponse<CreditCheckResponse>, number>({
      query: (required) => ({
        url: `check?required=${required}`,
      }),
    }),
  }),
});

export const { useGetCreditBalanceQuery, useGetCreditHistoryQuery, useCheckCreditsQuery } =
  creditApis;
