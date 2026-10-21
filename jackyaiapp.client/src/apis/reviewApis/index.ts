import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { refreshLearningStats } from '../statsApis';
import { ApiOkResponse } from '../types';
import { alsoRefresh, type SliceRefresh } from '../utils/alsoRefresh';

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
      // The weekly report counts the words reviewed.
      onQueryStarted: alsoRefresh(refreshLearningStats),
    }),
    getDueCount: builder.query<ApiOkResponse<DueCountResponse>, void>({
      query: () => 'count',
      providesTags: ['DueCount'],
    }),
  }),
});

/** For mutations in other slices that change which words are due (adding or removing a word,
 * recording an exam result, which reschedules the word). */
export const refreshReviewQueue: SliceRefresh = (dispatch) =>
  dispatch(reviewApis.util.invalidateTags(['DueReviews', 'DueCount']));

export const { useGetDueReviewsQuery, useSubmitReviewsMutation, useGetDueCountQuery } = reviewApis;
