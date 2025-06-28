// filepath: e:\Users\jacky\Documents\Repo\JackyAIApp\jackyaiapp.client\src\apis\financeApis\index.ts

import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { ApiOkResponse } from '../types';
import { StrategicInsight } from './types';

// Define a service using a base URL and expected endpoints
export const financeApis = createApi({
  reducerPath: 'financeApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/finance',
  }),
  tagTypes: ['DailyInfo'],
  endpoints: (builder) => ({
    getDailyImportantInfo: builder.query<ApiOkResponse<StrategicInsight[]>, void>({
      query: () => ({
        url: `dailyimportantinfo`,
      }),
      providesTags: ['DailyInfo'],
      // Force refetch on errors by not caching failed requests
      keepUnusedDataFor: 0, // Don't cache failed requests
      // Retry failed requests
      transformErrorResponse: (response, meta, arg) => {
        // Log error for debugging
        console.error('Finance API error:', response);
        return response;
      },
    }),
  }),
});

export const { useGetDailyImportantInfoQuery } = financeApis;
