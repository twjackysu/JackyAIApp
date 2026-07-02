import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { ChatConversation, ChatHistoryMessage } from './types';

export const chatbotApis = createApi({
  reducerPath: 'chatbotApis',
  baseQuery: apiBaseQuery('/api/chatbot'),
  tagTypes: ['Conversations'],
  endpoints: (builder) => ({
    /** List the current user's conversations (most-recently updated first). */
    getConversations: builder.query<ApiOkResponse<ChatConversation[]>, void>({
      query: () => 'conversations',
      providesTags: ['Conversations'],
    }),

    /** Load the message history for a single conversation (oldest → newest). */
    getConversationMessages: builder.query<ApiOkResponse<ChatHistoryMessage[]>, string>({
      query: (conversationId) => `conversations/${conversationId}/messages`,
    }),

    /** Delete one of the current user's conversations. */
    deleteConversation: builder.mutation<ApiOkResponse<void>, string>({
      query: (conversationId) => ({ url: `conversations/${conversationId}`, method: 'DELETE' }),
      invalidatesTags: ['Conversations'],
    }),
  }),
});

export const {
  useGetConversationsQuery,
  useLazyGetConversationMessagesQuery,
  useDeleteConversationMutation,
} = chatbotApis;
