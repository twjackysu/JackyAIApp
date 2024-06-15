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
    getWords: builder.query<ApiOkResponse<Word[]>, void>({
      query: () => ({
        url: 'word',
      }),
      providesTags: (result) =>
        result
          ? [
              ...result.data.map((x) => ({ type: 'PersonalWord' as const, id: x.word })),
              'PersonalWord',
            ]
          : ['PersonalWord'],
    }),
    getWord: builder.query<ApiOkResponse<Word>, string>({
      query: (personalWordId) => ({
        url: `word/${personalWordId}`,
      }),
      providesTags: (result) =>
        result
          ? [{ type: 'PersonalWord' as const, id: result.data.word }, 'PersonalWord']
          : ['PersonalWord'],
    }),
    putWord: builder.mutation<ApiOkResponse<PersonalWord>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
        method: 'PUT',
        invalidatesTags: ['PersonalWord'],
      }),
    }),
  }),
});

export const { useGetWordQuery, useGetWordsQuery, usePutWordMutation } = repositoryApis;
