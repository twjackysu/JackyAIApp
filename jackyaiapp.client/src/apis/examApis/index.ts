import { ClozeTest, TranslationTest } from '@/apis/dictionaryApis/types';
import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { ApiOkResponse } from '../types';

// Define a service using a base URL and expected endpoints
export const examApis = createApi({
  reducerPath: 'examApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/exam',
  }),
  endpoints: (builder) => ({
    getClozeTest: builder.query<ApiOkResponse<ClozeTest>, void>({
      query: () => ({
        url: 'cloze',
      }),
    }),
    getTranslationTest: builder.query<ApiOkResponse<TranslationTest>, void>({
      query: () => ({
        url: 'translation',
      }),
    }),
  }),
});

export const { useGetClozeTestQuery, useGetTranslationTestQuery } = examApis;
