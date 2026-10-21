import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { refreshReviewQueue } from '../reviewApis';
import { refreshLearningStats } from '../statsApis';
import { ApiOkResponse } from '../types';
import { alsoRefresh } from '../utils/alsoRefresh';

import { AddWordPackResult, GetRepositoryWordRequest, WordPack } from './types';

import { Word } from '@/apis/dictionaryApis/types';

// Define a service using a base URL and expected endpoints
export const repositoryApis = createApi({
  reducerPath: 'repositoryApis',
  tagTypes: ['PersonalWord'],
  baseQuery: apiBaseQuery('/api/repository'),
  endpoints: (builder) => ({
    getRepositoryWords: builder.query<ApiOkResponse<Word[]>, GetRepositoryWordRequest>({
      query: (request) => ({
        url: `word?pageNumber=${request.pageNumber}&pageSize=${request.pageSize}`,
      }),
      providesTags: (result) =>
        result
          ? [
              ...result.data.map(({ id }) => ({ type: 'PersonalWord' as const, id })),
              'PersonalWord',
            ]
          : ['PersonalWord'],
    }),
    getRepositoryWordsByWordId: builder.query<ApiOkResponse<Word>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
      }),
      providesTags: (result, _, arg) =>
        result ? [{ type: 'PersonalWord' as const, id: arg }, 'PersonalWord'] : ['PersonalWord'],
    }),
    putRepositoryWord: builder.mutation<ApiOkResponse<void>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
        method: 'PUT',
      }),
      invalidatesTags: ['PersonalWord'],
      // A new word is due for review at once, and counts as a new word in the weekly report.
      onQueryStarted: alsoRefresh(refreshReviewQueue, refreshLearningStats),
    }),
    deleteRepositoryWord: builder.mutation<ApiOkResponse<void>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_, __, arg) => [{ type: 'PersonalWord', id: arg }],
      onQueryStarted: alsoRefresh(refreshReviewQueue, refreshLearningStats),
    }),
    getWordPacks: builder.query<ApiOkResponse<WordPack[]>, void>({
      query: () => ({ url: 'packs' }),
    }),
    addWordPack: builder.mutation<ApiOkResponse<AddWordPackResult>, string>({
      query: (packId) => ({
        url: `packs/${packId}`,
        method: 'POST',
      }),
      invalidatesTags: ['PersonalWord'],
      onQueryStarted: alsoRefresh(refreshReviewQueue, refreshLearningStats),
    }),
  }),
});

export const {
  useGetRepositoryWordsQuery,
  useGetRepositoryWordsByWordIdQuery,
  usePutRepositoryWordMutation,
  useDeleteRepositoryWordMutation,
  useGetWordPacksQuery,
  useAddWordPackMutation,
} = repositoryApis;
