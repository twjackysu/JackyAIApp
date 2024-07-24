import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { Word } from '@/apis/dictionaryApis/types';
import { ApiOkResponse } from '../types';
import { PersonalWord } from './types';

// Define a service using a base URL and expected endpoints
export const repositoryApis = createApi({
  reducerPath: 'repositoryApis',
  tagTypes: ['PersonalWord'],
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/repository',
  }),
  endpoints: (builder) => ({
    getRepositoryWords: builder.query<ApiOkResponse<Word[]>, void>({
      query: () => ({
        url: 'word',
      }),
      providesTags: ['PersonalWord'],
    }),
    putRepositoryWord: builder.mutation<ApiOkResponse<PersonalWord>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
        method: 'PUT',
        invalidatesTags: ['PersonalWord'],
      }),
    }),
    deleteRepositoryWord: builder.mutation<ApiOkResponse<void>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
        method: 'DELETE',
        invalidatesTags: ['PersonalWord'],
      }),
    }),
  }),
});

export const {
  useGetRepositoryWordsQuery,
  usePutRepositoryWordMutation,
  useDeleteRepositoryWordMutation,
} = repositoryApis;
