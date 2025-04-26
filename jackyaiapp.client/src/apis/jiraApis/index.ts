import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { ApiOkResponse } from '../types';
import { JiraSearchRequest, JiraSearchResponse } from './types';

// Define a service using a base URL and expected endpoints
export const jiraApis = createApi({
  reducerPath: 'jiraApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/jira',
  }),
  endpoints: (builder) => ({
    postSearch: builder.query<ApiOkResponse<JiraSearchResponse>, JiraSearchRequest>({
      query: ({ body }) => ({
        url: `search`,
        method: 'POST',
        body,
      }),
    }),
  }),
});

export const { useLazyPostSearchQuery } = jiraApis;
