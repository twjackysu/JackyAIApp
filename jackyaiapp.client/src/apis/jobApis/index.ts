import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { JobListing, JobSearchParams, PlatformInfo } from './types';

export const jobApis = createApi({
  reducerPath: 'jobApis',
  baseQuery: apiBaseQuery('/api/job'),
  tagTypes: ['Listings'],
  endpoints: (builder) => ({
    getListings: builder.query<ApiOkResponse<JobListing[]>, JobSearchParams>({
      query: ({ keywords, strategy, platforms }) => ({
        url: 'listings',
        params: {
          keywords,
          strategy,
          platforms: platforms.join(','),
        },
      }),
      providesTags: ['Listings'],
      keepUnusedDataFor: 0, // always refetch on manual refresh
    }),
    getPlatforms: builder.query<ApiOkResponse<PlatformInfo[]>, void>({
      query: () => 'platforms',
      keepUnusedDataFor: 3600,
    }),
  }),
});

export const { useGetListingsQuery, useGetPlatformsQuery } = jobApis;
