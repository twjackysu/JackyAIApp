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
  endpoints: (builder) => ({
    getDailyImportantInfo: builder.query<ApiOkResponse<StrategicInsight[]>, void>({
      query: () => ({
        url: `dailyimportantinfo`,
      }),
    }),
  }),
});

export const { useGetDailyImportantInfoQuery } = financeApis;
