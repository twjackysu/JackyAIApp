import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { refreshLearningStats } from '../statsApis';
import { ApiOkResponse } from '../types';
import { alsoRefresh } from '../utils/alsoRefresh';

import {
  DailyChallengeResponse,
  DailyChallengeSubmitRequest,
  DailyChallengeSubmitResponse,
  DailyChallengeStatsResponse,
} from './types';

export const dailyChallengeApis = createApi({
  reducerPath: 'dailyChallengeApis',
  baseQuery: apiBaseQuery('/api/dailychallenge'),
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
      // 'Stats' above is this slice's own streak/XP card; the weekly report, leaderboard and
      // activity heatmap live in statsApis.
      onQueryStarted: alsoRefresh(refreshLearningStats),
    }),
    getStats: builder.query<ApiOkResponse<DailyChallengeStatsResponse>, void>({
      query: () => 'stats',
      providesTags: ['Stats'],
    }),
  }),
});

export const { useGetChallengeQuery, useSubmitChallengeMutation, useGetStatsQuery } =
  dailyChallengeApis;
