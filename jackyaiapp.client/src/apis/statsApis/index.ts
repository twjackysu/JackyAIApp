import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';

import { WeeklyReportResponse, LeaderboardResponse, ActivityResponse } from './types';

export const statsApis = createApi({
  reducerPath: 'statsApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/stats',
  }),
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
