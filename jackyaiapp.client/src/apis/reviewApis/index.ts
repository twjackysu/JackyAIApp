import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import {
  DueReviewsResponse,
  ReviewSubmitRequest,
  ReviewSubmitResponse,
  DueCountResponse,
} from './types';

export const reviewApis = createApi({
  reducerPath: 'reviewApis',
  baseQuery: apiBaseQuery('/api/review'),
  tagTypes: ['DueReviews', 'DueCount'],
  endpoints: (builder) => ({
    getDueReviews: builder.query<ApiOkResponse<DueReviewsResponse>, void>({
      query: () => 'due',
      providesTags: ['DueReviews'],
    }),
    submitReviews: builder.mutation<ApiOkResponse<ReviewSubmitResponse>, ReviewSubmitRequest>({
      query: (body) => ({
        url: 'submit',
        method: 'POST',
        body,
      }),
      invalidatesTags: ['DueReviews', 'DueCount'],
    }),
    getDueCount: builder.query<ApiOkResponse<DueCountResponse>, void>({
      query: () => 'count',
      providesTags: ['DueCount'],
    }),
  }),
});

export const { useGetDueReviewsQuery, useSubmitReviewsMutation, useGetDueCountQuery } = reviewApis;
