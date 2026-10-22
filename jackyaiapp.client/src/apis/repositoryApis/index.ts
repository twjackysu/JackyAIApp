import { api, onSuccess } from '../api';
import { ApiOkResponse } from '../types';

import { AddWordPackResult, GetRepositoryWordRequest, WordPack } from './types';

import { Word } from '@/apis/dictionaryApis/types';

// A word added or removed changes which words are due for review, and the weekly report's
// new-word count.
export const repositoryApis = api.injectEndpoints({
  endpoints: (builder) => ({
    getRepositoryWords: builder.query<ApiOkResponse<Word[]>, GetRepositoryWordRequest>({
      query: (request) => ({
        url: `repository/word?pageNumber=${request.pageNumber}&pageSize=${request.pageSize}`,
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
        url: `repository/word/${wordId}`,
      }),
      providesTags: (result, _, arg) =>
        result ? [{ type: 'PersonalWord' as const, id: arg }, 'PersonalWord'] : ['PersonalWord'],
    }),
    putRepositoryWord: builder.mutation<ApiOkResponse<void>, string>({
      query: (wordId) => ({
        url: `repository/word/${wordId}`,
        method: 'PUT',
      }),
      invalidatesTags: onSuccess('PersonalWord', 'DueReviews', 'DueCount', 'LearningStats'),
    }),
    deleteRepositoryWord: builder.mutation<ApiOkResponse<void>, string>({
      query: (wordId) => ({
        url: `repository/word/${wordId}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_result, error, wordId) =>
        error
          ? []
          : [{ type: 'PersonalWord', id: wordId }, 'DueReviews', 'DueCount', 'LearningStats'],
    }),
    getWordPacks: builder.query<ApiOkResponse<WordPack[]>, void>({
      query: () => ({ url: 'repository/packs' }),
    }),
    addWordPack: builder.mutation<ApiOkResponse<AddWordPackResult>, string>({
      query: (packId) => ({
        url: `repository/packs/${packId}`,
        method: 'POST',
      }),
      invalidatesTags: onSuccess('PersonalWord', 'DueReviews', 'DueCount', 'LearningStats'),
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
