import { Word } from '@/apis/dictionaryApis/types';
import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { ApiOkResponse } from '../types';
import { GetRepositoryWordRequest, PersonalWord } from './types';

// Define a service using a base URL and expected endpoints
export const repositoryApis = createApi({
  reducerPath: 'repositoryApis',
  tagTypes: ['PersonalWord'],
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/repository',
  }),
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
  }),
});

export const {
  useGetRepositoryWordsQuery,
  useGetRepositoryWordsByWordIdQuery,
  usePutRepositoryWordMutation,
  useDeleteRepositoryWordMutation,
} = repositoryApis;
