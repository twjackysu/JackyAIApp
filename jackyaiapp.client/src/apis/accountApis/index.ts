import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';

import { User } from './type';

// Define a service using a base URL and expected endpoints
export const accountApis = createApi({
  reducerPath: 'accountApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/account',
  }),
  endpoints: (builder) => ({
    getUserInfo: builder.query<ApiOkResponse<User>, void>({
      query: () => ({
        url: 'info',
      }),
      keepUnusedDataFor: 86400,
    }),
  }),
});

export const { useGetUserInfoQuery } = accountApis;
