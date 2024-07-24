import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { CheckAuthResponse } from './type';

// Define a service using a base URL and expected endpoints
export const accountApis = createApi({
  reducerPath: 'accountApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/account',
  }),
  endpoints: (builder) => ({
    checkAuth: builder.query<CheckAuthResponse, void>({
      query: () => ({
        url: 'check-auth',
      }),
    }),
  }),
});

export const { useCheckAuthQuery } = accountApis;
