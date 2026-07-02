import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { WeeklyReportResponse, LeaderboardResponse, ActivityResponse } from './types';

export const statsApis = createApi({
  reducerPath: 'statsApis',
  baseQuery: apiBaseQuery('/api/stats'),
  endpoints: (builder) => ({
    getWeeklyReport: builder.query<ApiOkResponse<WeeklyReportResponse>, void>({
      query: () => 'weekly-report',
    }),
    getLeaderboard: builder.query<ApiOkResponse<LeaderboardResponse>, number | void>({
      query: (limit) => `leaderboard${limit ? `?limit=${limit}` : ''}`,
    }),
    getActivity: builder.query<ApiOkResponse<ActivityResponse>, number | void>({
      query: (days) => `activity${days ? `?days=${days}` : ''}`,
    }),
  }),
});

export const { useGetWeeklyReportQuery, useGetLeaderboardQuery, useGetActivityQuery } = statsApis;
