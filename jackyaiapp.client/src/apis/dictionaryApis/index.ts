import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { Word } from './types';
import { ApiOkResponse } from '../types';

// Define a service using a base URL and expected endpoints
export const dictionaryApis = createApi({
  reducerPath: 'dictionaryApis',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/dictionary' }),
  endpoints: (builder) => ({
    getWord: builder.query<ApiOkResponse<Word>, string>({
      query: (word) => word,
    }),
  }),
});

export const { useGetWordQuery } = dictionaryApis;
