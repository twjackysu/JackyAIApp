import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';

import { WeeklyReportResponse, LeaderboardResponse } from './types';

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
  }),
});

export const { useGetWeeklyReportQuery, useGetLeaderboardQuery } = statsApis;
