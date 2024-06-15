import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { Word } from './types';
import { ApiOkResponse } from '../types';

// Define a service using a base URL and expected endpoints
export const dictionaryApis = createApi({
  reducerPath: 'dictionaryApis',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/dictionary' }),
  tagTypes: ['Word'],
  endpoints: (builder) => ({
    getWord: builder.query<ApiOkResponse<Word>, string>({
      query: (word) => word,
      providesTags: (result) =>
        result ? [{ type: 'Word' as const, id: result.data.word }, 'Word'] : ['Word'],
    }),
    invalidWord: builder.mutation<ApiOkResponse<Word>, string>({
      query: (word) => ({
        url: `${word}/invalid`,
        method: 'PUT',
      }),
      invalidatesTags: (result) => [{ type: 'Word', id: result?.data.word }],
    }),
  }),
});

export const { useGetWordQuery, useInvalidWordMutation } = dictionaryApis;
