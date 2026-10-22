import { api, onSuccess } from '../api';
import { ApiOkResponse } from '../types';

import {
  DueReviewsResponse,
  ReviewSubmitRequest,
  ReviewSubmitResponse,
  DueCountResponse,
} from './types';

export const reviewApis = api.injectEndpoints({
  endpoints: (builder) => ({
    getDueReviews: builder.query<ApiOkResponse<DueReviewsResponse>, void>({
      query: () => 'review/due',
      providesTags: ['DueReviews'],
    }),
    submitReviews: builder.mutation<ApiOkResponse<ReviewSubmitResponse>, ReviewSubmitRequest>({
      query: (body) => ({
        url: 'review/submit',
        method: 'POST',
        body,
      }),
      // The weekly report counts the words reviewed, and a correct answer earns XP.
      invalidatesTags: onSuccess('DueReviews', 'DueCount', 'LearningStats'),
    }),
    getDueCount: builder.query<ApiOkResponse<DueCountResponse>, void>({
      query: () => 'review/count',
      providesTags: ['DueCount'],
    }),
  }),
});

export const { useGetDueReviewsQuery, useSubmitReviewsMutation, useGetDueCountQuery } = reviewApis;
