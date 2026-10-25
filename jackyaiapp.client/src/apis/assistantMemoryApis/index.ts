import { api, onSuccess } from '../api';
import { ApiOkResponse } from '../types';

import { AssistantMemoryList } from './types';

/** What the reader asked Studio AI to remember: listed and forgotten on the connectors page (design/08 §6). */
export const assistantMemoryApis = api.injectEndpoints({
  endpoints: (builder) => ({
    getAssistantMemories: builder.query<ApiOkResponse<AssistantMemoryList>, void>({
      query: () => 'assistant/memories',
      providesTags: ['AssistantMemory'],
    }),
    forgetAssistantMemory: builder.mutation<ApiOkResponse<null>, string>({
      query: (id) => ({ url: `assistant/memories/${encodeURIComponent(id)}`, method: 'DELETE' }),
      invalidatesTags: onSuccess('AssistantMemory'),
    }),
  }),
});

export const { useGetAssistantMemoriesQuery, useForgetAssistantMemoryMutation } =
  assistantMemoryApis;
