import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { ChatAttachment, ChatConversation, ChatHistoryMessage } from './types';

export const chatbotApis = createApi({
  reducerPath: 'chatbotApis',
  baseQuery: apiBaseQuery('/api/chatbot'),
  tagTypes: ['Conversations'],
  endpoints: (builder) => ({
    /** List the current user's conversations with one assistant (a chat profile), most recently updated first. */
    getConversations: builder.query<ApiOkResponse<ChatConversation[]>, string>({
      query: (profile) => ({ url: 'conversations', params: { profile } }),
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

    /**
     * Have the AI name a new conversation from its first question and answer (D-141), asked once after its first
     * answer. The server names it only the first time; the list is read again for the new title.
     */
    nameConversation: builder.mutation<ApiOkResponse<{ title: string }>, string>({
      query: (conversationId) => ({
        url: `conversations/${encodeURIComponent(conversationId)}/title`,
        method: 'POST',
      }),
      invalidatesTags: (result) => (result ? ['Conversations'] : []),
    }),

    /** Upload a file to send with the next message; the server reads it into text. */
    uploadChatAttachment: builder.mutation<ApiOkResponse<ChatAttachment>, File>({
      query: (file) => {
        const body = new FormData();
        body.append('file', file);
        return { url: 'attachments', method: 'POST', body };
      },
    }),

    /** Remove an uploaded file that has not been sent. */
    deleteChatAttachment: builder.mutation<ApiOkResponse<void>, number>({
      query: (attachmentId) => ({ url: `attachments/${attachmentId}`, method: 'DELETE' }),
    }),
  }),
});

export const {
  useGetConversationsQuery,
  useLazyGetConversationMessagesQuery,
  useDeleteConversationMutation,
  useNameConversationMutation,
  useUploadChatAttachmentMutation,
  useDeleteChatAttachmentMutation,
} = chatbotApis;
