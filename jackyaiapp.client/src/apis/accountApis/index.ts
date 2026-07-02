import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { User } from './type';

// Define a service using a base URL and expected endpoints
export const accountApis = createApi({
  reducerPath: 'accountApis',
  baseQuery: apiBaseQuery('/api/account'),
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
