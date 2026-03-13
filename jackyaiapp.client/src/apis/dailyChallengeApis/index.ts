import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';

import {
  DailyChallengeResponse,
  DailyChallengeSubmitRequest,
  DailyChallengeSubmitResponse,
  DailyChallengeStatsResponse,
} from './types';

export const dailyChallengeApis = createApi({
  reducerPath: 'dailyChallengeApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/dailychallenge',
  }),
  tagTypes: ['DailyChallenge', 'Stats'],
  endpoints: (builder) => ({
    getChallenge: builder.query<ApiOkResponse<DailyChallengeResponse>, void>({
      query: () => '',
      providesTags: ['DailyChallenge'],
    }),
    submitChallenge: builder.mutation<
      ApiOkResponse<DailyChallengeSubmitResponse>,
      DailyChallengeSubmitRequest
    >({
      query: (body) => ({
        url: 'submit',
        method: 'POST',
        body,
      }),
      invalidatesTags: ['DailyChallenge', 'Stats'],
    }),
    getStats: builder.query<ApiOkResponse<DailyChallengeStatsResponse>, void>({
      query: () => 'stats',
      providesTags: ['Stats'],
    }),
    claimDailyBonus: builder.mutation<
      ApiOkResponse<{ claimed: boolean; credits?: number; message?: string }>,
      void
    >({
      query: () => ({
        url: 'claim-daily-bonus',
        method: 'POST',
      }),
      invalidatesTags: ['Stats'],
    }),
  }),
});

export const {
  useGetChallengeQuery,
  useSubmitChallengeMutation,
  useGetStatsQuery,
  useClaimDailyBonusMutation,
} = dailyChallengeApis;
