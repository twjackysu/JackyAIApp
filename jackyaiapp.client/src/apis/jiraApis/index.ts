import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { ApiOkResponse } from '../types';
import { JiraConfig, JiraConfigRequest, JiraSearchRequest, JiraSearchResponse } from './types';

// Define a service using a base URL and expected endpoints
export const jiraApis = createApi({
  reducerPath: 'jiraApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/jira',
  }),
  tagTypes: ['JiraConfig'],
  endpoints: (builder) => ({
    postSearch: builder.query<ApiOkResponse<JiraSearchResponse>, JiraSearchRequest>({
      query: ({ body }) => ({
        url: `search`,
        method: 'POST',
        body,
      }),
    }),
    postJiraConfig: builder.mutation<ApiOkResponse<string>, JiraConfigRequest>({
      query: ({ body }) => ({
        url: `configs`,
        method: 'POST',
        body,
      }),
      invalidatesTags: ['JiraConfig'],
    }),
    getJiraConfig: builder.query<ApiOkResponse<JiraConfig[]>, void>({
      query: () => ({
        url: `configs`,
        method: 'GET',
      }),
      providesTags: ['JiraConfig'],
    }),
    deleteJiraConfig: builder.mutation<void, string>({
      query: (id) => ({
        url: `configs/${id}`,
        method: 'DELETE',
      }),
      invalidatesTags: ['JiraConfig'],
    }),
  }),
});

export const {
  useLazyPostSearchQuery,
  useGetJiraConfigQuery,
  usePostJiraConfigMutation,
  useDeleteJiraConfigMutation,
} = jiraApis;
