import { api, onSuccess } from '../api';
import { ApiOkResponse } from '../types';

import {
  DailyChallengeResponse,
  DailyChallengeSubmitRequest,
  DailyChallengeSubmitResponse,
  DailyChallengeStatsResponse,
} from './types';

export const dailyChallengeApis = api.injectEndpoints({
  endpoints: (builder) => ({
    getChallenge: builder.query<ApiOkResponse<DailyChallengeResponse>, void>({
      query: () => 'dailychallenge',
      providesTags: ['DailyChallenge'],
    }),
    submitChallenge: builder.mutation<
      ApiOkResponse<DailyChallengeSubmitResponse>,
      DailyChallengeSubmitRequest
    >({
      query: (body) => ({
        url: 'dailychallenge/submit',
        method: 'POST',
        body,
      }),
      invalidatesTags: onSuccess('DailyChallenge', 'LearningStats'),
    }),
    // The streak/XP card: XP from reviews and the streak from challenges both change it, so it is
    // part of the learning stats rather than of the challenge.
    getStats: builder.query<ApiOkResponse<DailyChallengeStatsResponse>, void>({
      query: () => 'dailychallenge/stats',
      providesTags: ['LearningStats'],
    }),
  }),
});

export const { useGetChallengeQuery, useSubmitChallengeMutation, useGetStatsQuery } =
  dailyChallengeApis;
