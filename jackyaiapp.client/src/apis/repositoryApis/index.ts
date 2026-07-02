import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { AddWordPackResult, GetRepositoryWordRequest, PersonalWord, WordPack } from './types';

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
    putRepositoryWord: builder.mutation<ApiOkResponse<PersonalWord>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
        method: 'PUT',
      }),
      invalidatesTags: ['PersonalWord'],
    }),
    deleteRepositoryWord: builder.mutation<ApiOkResponse<void>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_, __, arg) => [{ type: 'PersonalWord', id: arg }],
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
