import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { ApiOkResponse } from '../types';

export const microsoftGraphApis = createApi({
  reducerPath: 'microsoftGraphApis',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/microsoftgraph',
  }),
  tagTypes: ['MicrosoftGraphStatus', 'Emails', 'Calendar', 'Teams', 'Chats'],
  endpoints: (builder) => ({
    // Get auth URL for Microsoft Graph OAuth
    getMicrosoftGraphAuthUrl: builder.query<ApiOkResponse<{
      authUrl: string;
    }>, void>({
      query: () => '/auth-url',
    }),

    // Get connection status
    getMicrosoftGraphStatus: builder.query<ApiOkResponse<{
      isConnected: boolean;
      connectedAt?: string;
      lastUpdated?: string;
      scopes?: string[];
    }>, void>({
      query: () => '/status',
      providesTags: ['MicrosoftGraphStatus'],
    }),

    // Disconnect Microsoft Graph
    disconnectMicrosoftGraph: builder.mutation<ApiOkResponse<{ message: string }>, void>({
      query: () => ({
        url: '/disconnect',
        method: 'POST',
      }),
      invalidatesTags: ['MicrosoftGraphStatus', 'Emails', 'Calendar', 'Teams', 'Chats'],
    }),

    // Get emails
    getEmails: builder.query<ApiOkResponse<{
      totalCount: number;
      emails: Array<{
        subject: string;
        from: string;
        fromEmail: string;
        receivedDateTime: string;
        bodyPreview: string;
        importance: string;
      }>;
    }>, void>({
      query: () => '/emails',
      providesTags: ['Emails'],
    }),

    // Get calendar events
    getCalendarEvents: builder.query<ApiOkResponse<{
      totalCount: number;
      events: Array<{
        subject: string;
        start: {
          dateTime: string;
          timeZone: string;
        };
        end: {
          dateTime: string;
          timeZone: string;
        };
        location: string;
        organizer: string;
        attendeesCount: number;
      }>;
    }>, void>({
      query: () => '/calendar',
      providesTags: ['Calendar'],
    }),

    // Get Teams
    getTeams: builder.query<ApiOkResponse<{
      totalCount: number;
      teams: Array<{
        id: string;
        displayName: string;
        description: string;
        createdDateTime: string;
      }>;
    }>, void>({
      query: () => '/teams',
      providesTags: ['Teams'],
    }),

    // Get Chats
    getChats: builder.query<ApiOkResponse<{
      totalCount: number;
      chats: Array<{
        id: string;
        topic: string;
        createdDateTime: string;
        lastUpdatedDateTime: string;
        chatType: string;
      }>;
    }>, void>({
      query: () => '/chats',
      providesTags: ['Chats'],
    }),
  }),
});

export const {
  useGetMicrosoftGraphAuthUrlQuery,
  useGetMicrosoftGraphStatusQuery,
  useDisconnectMicrosoftGraphMutation,
  useGetEmailsQuery,
  useGetCalendarEventsQuery,
  useGetTeamsQuery,
  useGetChatsQuery,
} = microsoftGraphApis;