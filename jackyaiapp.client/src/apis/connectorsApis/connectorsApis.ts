import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';

import {
  ConnectorStatus,
  ConnectResponse,
  RefreshResponse,
  DisconnectResponse,
  AccessTokenResponse,
  RefreshTokenResponse,
  CustomConnectRequest,
  ConnectorCatalog,
  McpProbeResult,
  CreateInstallationRequest,
  InstalledConnector,
  ConnectionTestResult,
  ToolPermissionRequest,
} from './types';

/** Refetch the catalog only after a write the server accepted (one it refused changed nothing). */
const refreshCatalog = (_result: unknown, error: unknown): 'ConnectorCatalog'[] =>
  error ? [] : ['ConnectorCatalog'];

export const connectorsApi = createApi({
  reducerPath: 'connectorsApi',
  baseQuery: apiBaseQuery('/api/connectors/', 'include'), // Include cookies for authentication
  tagTypes: ['ConnectorStatus', 'ConnectorCatalog'],
  endpoints: (builder) => ({
    getConnectorStatus: builder.query<ConnectorStatus[], void>({
      query: () => 'status',
      providesTags: ['ConnectorStatus'],
    }),
    connectProvider: builder.mutation<
      ConnectResponse,
      { provider: string; customConfig?: CustomConnectRequest }
    >({
      query: ({ provider, customConfig }) => ({
        url: `${provider}/connect`,
        method: 'POST',
        body: customConfig || {},
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
    getRefreshToken: builder.query<RefreshTokenResponse, string>({
      query: (provider) => `${provider}/refresh-token`,
    }),

    // AI connectors: what the AI may use (docs/ai-native/design/03 §7).
    getConnectorCatalog: builder.query<ConnectorCatalog, void>({
      query: () => 'catalog',
      providesTags: ['ConnectorCatalog'],
    }),
    probeMcpServer: builder.mutation<McpProbeResult, { serverUrl: string }>({
      query: (body) => ({ url: 'installations/probe', method: 'POST', body }),
    }),
    createInstallation: builder.mutation<InstalledConnector, CreateInstallationRequest>({
      query: (body) => ({ url: 'installations', method: 'POST', body }),
      invalidatesTags: refreshCatalog,
    }),
    authorizeInstallation: builder.mutation<ConnectResponse, string>({
      query: (id) => ({ url: `installations/${id}/authorize`, method: 'POST' }),
    }),
    testInstallation: builder.mutation<ConnectionTestResult, string>({
      query: (id) => ({ url: `installations/${id}/test`, method: 'POST' }),
      invalidatesTags: refreshCatalog,
    }),
    setInstallationEnabled: builder.mutation<InstalledConnector, { id: string; enabled: boolean }>({
      query: ({ id, enabled }) => ({
        url: `installations/${id}/enabled`,
        method: 'PUT',
        body: { enabled },
      }),
      invalidatesTags: refreshCatalog,
    }),
    removeInstallation: builder.mutation<void, string>({
      query: (id) => ({ url: `installations/${id}`, method: 'DELETE' }),
      invalidatesTags: refreshCatalog,
    }),
    setToolPermission: builder.mutation<InstalledConnector, ToolPermissionRequest>({
      query: (body) => ({ url: 'tool-permissions', method: 'PUT', body }),
      invalidatesTags: refreshCatalog,
    }),
  }),
});

export const {
  useGetConnectorStatusQuery,
  useConnectProviderMutation,
  useDisconnectProviderMutation,
  useRefreshProviderTokensMutation,
  useLazyGetAccessTokenQuery,
  useLazyGetRefreshTokenQuery,
  useGetConnectorCatalogQuery,
  useProbeMcpServerMutation,
  useCreateInstallationMutation,
  useAuthorizeInstallationMutation,
  useTestInstallationMutation,
  useSetInstallationEnabledMutation,
  useRemoveInstallationMutation,
  useSetToolPermissionMutation,
} = connectorsApi;
