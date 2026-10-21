import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { WeeklyReportResponse, LeaderboardResponse, ActivityResponse } from './types';

import type { SliceRefresh } from '../utils/alsoRefresh';

export const statsApis = createApi({
  reducerPath: 'statsApis',
  baseQuery: apiBaseQuery('/api/stats'),
  // One tag for all three: words, reviews, challenges and exams each feed more than one of them.
  tagTypes: ['LearningStats'],
  endpoints: (builder) => ({
    getWeeklyReport: builder.query<ApiOkResponse<WeeklyReportResponse>, void>({
      query: () => 'weekly-report',
      providesTags: ['LearningStats'],
    }),
    getLeaderboard: builder.query<ApiOkResponse<LeaderboardResponse>, number | void>({
      query: (limit) => `leaderboard${limit ? `?limit=${limit}` : ''}`,
      providesTags: ['LearningStats'],
    }),
    getActivity: builder.query<ApiOkResponse<ActivityResponse>, number | void>({
      query: (days) => `activity${days ? `?days=${days}` : ''}`,
      providesTags: ['LearningStats'],
    }),
  }),
});

/** For mutations in other slices that change the weekly report, leaderboard or activity. */
export const refreshLearningStats: SliceRefresh = (dispatch) =>
  dispatch(statsApis.util.invalidateTags(['LearningStats']));

export const { useGetWeeklyReportQuery, useGetLeaderboardQuery, useGetActivityQuery } = statsApis;
