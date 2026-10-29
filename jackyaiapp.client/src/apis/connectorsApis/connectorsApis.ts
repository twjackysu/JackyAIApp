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
  McpRegistrySearch,
  CreateInstallationRequest,
  InstalledConnector,
  ConnectionTestResult,
  ToolPermissionRequest,
  SiteAccessTokenList,
  CreateSiteAccessTokenRequest,
  CreatedSiteAccessToken,
  McpApp,
  McpAppCallResult,
} from './types';

import type { ApiOkResponse } from '../types';

/** The new endpoints answer in the app's `{ data }` envelope (the older ones above do not). */
const unwrapData = <T>(res: ApiOkResponse<T>): T => res.data;

/** Refetch the catalog only after a write the server accepted (one it refused changed nothing). */
const refreshCatalog = (_result: unknown, error: unknown): 'ConnectorCatalog'[] =>
  error ? [] : ['ConnectorCatalog'];

const refreshSiteTokens = (_result: unknown, error: unknown): 'SiteTokens'[] =>
  error ? [] : ['SiteTokens'];

export const connectorsApi = createApi({
  reducerPath: 'connectorsApi',
  baseQuery: apiBaseQuery('/api/connectors/', 'include'), // Include cookies for authentication
  tagTypes: ['ConnectorStatus', 'ConnectorCatalog', 'SiteTokens'],
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
      transformResponse: unwrapData,
      providesTags: ['ConnectorCatalog'],
    }),
    /** The official MCP Registry's servers whose name holds the search (D-122); cached by the server. */
    searchMcpRegistry: builder.query<McpRegistrySearch, string>({
      query: (search) => ({ url: 'registry', params: search ? { search } : undefined }),
      transformResponse: unwrapData,
      keepUnusedDataFor: 600,
    }),
    /** A connector's MCP App (`ui://…`) for its card in a chat (design/09 §3); read from its server, kept a while. */
    getMcpApp: builder.query<McpApp, { installationId: string; uri: string }>({
      query: ({ installationId, uri }) => ({
        url: `installations/${encodeURIComponent(installationId)}/app`,
        params: { uri },
      }),
      transformResponse: unwrapData,
      keepUnusedDataFor: 600,
    }),
    /** A call a connector's app asks of its server (`tools/call` from the app): a tool that only reads, design/09 §3. */
    callFromMcpApp: builder.mutation<
      McpAppCallResult,
      {
        installationId: string;
        tool: string;
        arguments: Record<string, unknown>;
        /** The reader allowed this tool on the app's card (D-207). */
        readerAllowed?: boolean;
      }
    >({
      query: ({ installationId, tool, arguments: args, readerAllowed }) => ({
        url: `installations/${encodeURIComponent(installationId)}/app-call`,
        method: 'POST',
        body: { tool, arguments: args, readerAllowed: readerAllowed === true },
      }),
      transformResponse: unwrapData,
    }),
    probeMcpServer: builder.mutation<McpProbeResult, { serverUrl: string }>({
      query: (body) => ({ url: 'installations/probe', method: 'POST', body }),
      transformResponse: unwrapData,
    }),
    createInstallation: builder.mutation<InstalledConnector, CreateInstallationRequest>({
      query: (body) => ({ url: 'installations', method: 'POST', body }),
      transformResponse: unwrapData,
      invalidatesTags: refreshCatalog,
    }),
    authorizeInstallation: builder.mutation<ConnectResponse, string>({
      query: (id) => ({ url: `installations/${id}/authorize`, method: 'POST' }),
      transformResponse: unwrapData,
    }),
    testInstallation: builder.mutation<ConnectionTestResult, string>({
      query: (id) => ({ url: `installations/${id}/test`, method: 'POST' }),
      transformResponse: unwrapData,
      invalidatesTags: refreshCatalog,
    }),
    setInstallationEnabled: builder.mutation<InstalledConnector, { id: string; enabled: boolean }>({
      query: ({ id, enabled }) => ({
        url: `installations/${id}/enabled`,
        method: 'PUT',
        body: { enabled },
      }),
      transformResponse: unwrapData,
      invalidatesTags: refreshCatalog,
    }),
    removeInstallation: builder.mutation<void, string>({
      query: (id) => ({ url: `installations/${id}`, method: 'DELETE' }),
      invalidatesTags: refreshCatalog,
    }),
    setToolPermission: builder.mutation<InstalledConnector, ToolPermissionRequest>({
      query: (body) => ({ url: 'tool-permissions', method: 'PUT', body }),
      transformResponse: unwrapData,
      invalidatesTags: refreshCatalog,
    }),

    // Letting other AIs use the site: the tokens of its own MCP server (/api/mcp).
    getSiteTokens: builder.query<SiteAccessTokenList, void>({
      query: () => 'site-tokens',
      transformResponse: unwrapData,
      providesTags: ['SiteTokens'],
    }),
    createSiteToken: builder.mutation<CreatedSiteAccessToken, CreateSiteAccessTokenRequest>({
      query: (body) => ({ url: 'site-tokens', method: 'POST', body }),
      transformResponse: unwrapData,
      invalidatesTags: refreshSiteTokens,
    }),
    revokeSiteToken: builder.mutation<void, string>({
      query: (id) => ({ url: `site-tokens/${id}`, method: 'DELETE' }),
      invalidatesTags: refreshSiteTokens,
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
  useSearchMcpRegistryQuery,
  useGetMcpAppQuery,
  useCallFromMcpAppMutation,
  useProbeMcpServerMutation,
  useCreateInstallationMutation,
  useAuthorizeInstallationMutation,
  useTestInstallationMutation,
  useSetInstallationEnabledMutation,
  useRemoveInstallationMutation,
  useSetToolPermissionMutation,
  useGetSiteTokensQuery,
  useCreateSiteTokenMutation,
  useRevokeSiteTokenMutation,
} = connectorsApi;
