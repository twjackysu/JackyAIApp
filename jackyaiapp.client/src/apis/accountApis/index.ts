import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { CheckAuthResponse, User } from './type';

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
    getUserInfo: builder.query<User, void>({
      query: () => ({
        url: 'info',
      }),
      keepUnusedDataFor: 86400,
    }),
  }),
});

export const { useCheckAuthQuery, useGetUserInfoQuery } = accountApis;
