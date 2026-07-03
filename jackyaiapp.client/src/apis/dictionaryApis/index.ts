import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { Word, WordSuggestionsResponse } from './types';

// Define a service using a base URL and expected endpoints
export const dictionaryApis = createApi({
  reducerPath: 'dictionaryApis',
  baseQuery: apiBaseQuery('/api/dictionary'),
  tagTypes: ['Word'],
  endpoints: (builder) => ({
    getWord: builder.query<ApiOkResponse<Word>, string>({
      query: (word) => word,
      providesTags: (result) =>
        result ? [{ type: 'Word' as const, id: result.data.word }, 'Word'] : ['Word'],
    }),
    getWordOfTheDay: builder.query<ApiOkResponse<Word>, void>({
      query: () => 'WordOfTheDay',
    }),
    getWordSuggestions: builder.query<ApiOkResponse<WordSuggestionsResponse>, string>({
      query: (word) => `${word}/suggestions`,
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

export const {
  useGetWordQuery,
  useInvalidWordMutation,
  useGetWordOfTheDayQuery,
  useGetWordSuggestionsQuery,
} = dictionaryApis;
