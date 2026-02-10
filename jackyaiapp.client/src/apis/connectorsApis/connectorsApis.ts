import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ConnectorStatus, ConnectResponse, RefreshResponse, DisconnectResponse, AccessTokenResponse } from './types';

export const connectorsApi = createApi({
  reducerPath: 'connectorsApi',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/connectors/',
    credentials: 'include', // Include cookies for authentication
  }),
  tagTypes: ['ConnectorStatus'],
  endpoints: (builder) => ({
    getConnectorStatus: builder.query<ConnectorStatus[], void>({
      query: () => 'status',
      providesTags: ['ConnectorStatus'],
    }),
    connectProvider: builder.mutation<ConnectResponse, string>({
      query: (provider) => ({
        url: `${provider}/connect`,
        method: 'POST',
      }),
      invalidatesTags: ['ConnectorStatus'],
    }),
    disconnectProvider: builder.mutation<DisconnectResponse, string>({
      query: (provider) => ({
        url: provider,
        method: 'DELETE',
      }),
      invalidatesTags: ['ConnectorStatus'],
    }),
    refreshProviderTokens: builder.mutation<RefreshResponse, string>({
      query: (provider) => ({
        url: `${provider}/refresh`,
        method: 'POST',
      }),
      invalidatesTags: ['ConnectorStatus'],
    }),
    getAccessToken: builder.query<AccessTokenResponse, string>({
      query: (provider) => `${provider}/token`,
    }),
  }),
});

export const {
  useGetConnectorStatusQuery,
  useConnectProviderMutation,
  useDisconnectProviderMutation,
  useRefreshProviderTokensMutation,
  useLazyGetAccessTokenQuery,
} = connectorsApi;
