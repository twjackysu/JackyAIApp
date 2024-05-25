import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { Word } from '@/apis/dictionaryApis/types';
import { ApiOkResponse } from '../types';
import { PersonalWord } from './types';
import myBaseQuery from '../myBaseQuery';

// Define a service using a base URL and expected endpoints
export const repositoryApis = createApi({
  reducerPath: 'repositoryApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/repository',
  }),
  endpoints: (builder) => ({
    getWords: builder.query<ApiOkResponse<Word[]>, void>({
      query: () => ({
        url: 'word',
      }),
    }),
    getWord: builder.query<ApiOkResponse<Word>, string>({
      query: (personalWordId) => ({
        url: `word/${personalWordId}`,
      }),
    }),
    putWord: builder.mutation<ApiOkResponse<PersonalWord>, string>({
      query: (wordId) => ({
        url: `word/${wordId}`,
        method: 'PUT',
      }),
    }),
  }),
});

export const { useGetWordQuery, useGetWordsQuery, usePutWordMutation } = repositoryApis;
