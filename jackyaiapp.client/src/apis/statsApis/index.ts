import { api } from '../api';
import { ApiOkResponse } from '../types';

import { WeeklyReportResponse, LeaderboardResponse, ActivityResponse } from './types';

export const statsApis = api.injectEndpoints({
  endpoints: (builder) => ({
    getWeeklyReport: builder.query<ApiOkResponse<WeeklyReportResponse>, void>({
      query: () => 'stats/weekly-report',
      providesTags: ['LearningStats'],
    }),
    getLeaderboard: builder.query<ApiOkResponse<LeaderboardResponse>, number | void>({
      query: (limit) => `stats/leaderboard${limit ? `?limit=${limit}` : ''}`,
      providesTags: ['LearningStats'],
    }),
    getActivity: builder.query<ApiOkResponse<ActivityResponse>, number | void>({
      query: (days) => `stats/activity${days ? `?days=${days}` : ''}`,
      providesTags: ['LearningStats'],
    }),
  }),
});

export const { useGetWeeklyReportQuery, useGetLeaderboardQuery, useGetActivityQuery } = statsApis;
