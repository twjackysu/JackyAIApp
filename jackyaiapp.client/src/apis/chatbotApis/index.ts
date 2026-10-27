import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { ChatAttachment, ChatConversation, ChatHistoryMessage, ChatSearchHit } from './types';

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

    /**
     * Find the reader's conversations with one assistant by the words in their messages (D-191), most recently updated
     * first, each with a snippet around the find. Read again whenever the list is (a new answer may hold the words).
     */
    searchConversations: builder.query<
      ApiOkResponse<ChatSearchHit[]>,
      { profile: string; query: string }
    >({
      query: ({ profile, query }) => ({
        url: 'conversations/search',
        params: { profile, q: query },
      }),
      providesTags: ['Conversations'],
      keepUnusedDataFor: 30,
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

    /**
     * Give one of the reader's conversations a title of their own (D-144). The row shows it at once (the list of
     * `profile`'s conversations is patched), goes back to the old one if the server refuses, and is read again once saved.
     */
    renameConversation: builder.mutation<
      ApiOkResponse<{ title: string }>,
      { conversationId: string; title: string; profile: string }
    >({
      query: ({ conversationId, title }) => ({
        url: `conversations/${encodeURIComponent(conversationId)}/title`,
        method: 'PUT',
        body: { title },
      }),
      // Read again too: a list already being read when the rename went out would bring the old title back.
      invalidatesTags: (result) => (result ? ['Conversations'] : []),
      async onQueryStarted({ conversationId, title, profile }, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          chatbotApis.util.updateQueryData('getConversations', profile, (list) => {
            const row = list.data.find((c) => c.id === conversationId);
            if (row) row.title = title;
          }),
        );
        try {
          await queryFulfilled;
        } catch {
          patch.undo();
        }
      },
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
  useSearchConversationsQuery,
  useLazyGetConversationMessagesQuery,
  useDeleteConversationMutation,
  useNameConversationMutation,
  useRenameConversationMutation,
  useUploadChatAttachmentMutation,
  useDeleteChatAttachmentMutation,
} = chatbotApis;
