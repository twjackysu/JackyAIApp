import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import { buildRowCountRequest, sumRowCounts } from './rowCount';
import {
  DataSourceConfigDto,
  DetailsRequest,
  DetailsResult,
  DistinctValuesQueryParams,
  DistinctValuesResult,
  FieldListResult,
  MaxDateQueryParams,
  MaxDateResult,
  NlSummarizeRequest,
  NlSummarizeResult,
  RowCountQueryParams,
  SaveDataSourceConfigDto,
  SessionUploadRequest,
  SessionUploadResponse,
  SourceQueryParams,
  SummarizeRequest,
  SummarizeResult,
} from './types';

export const dataAnalysisApis = createApi({
  reducerPath: 'dataAnalysisApis',
  baseQuery: apiBaseQuery('/api/dataanalysis'),
  tagTypes: ['Sources'],
  endpoints: (builder) => ({
    // ── Source CRUD ─────────────────────────────────────────────────────────

    /** List all saved data-source connectors. */
    getSources: builder.query<ApiOkResponse<DataSourceConfigDto[]>, void>({
      query: () => 'sources',
      providesTags: ['Sources'],
    }),

    /** Save a new data-source connector. */
    createSource: builder.mutation<ApiOkResponse<DataSourceConfigDto>, SaveDataSourceConfigDto>({
      query: (body) => ({ url: 'sources', method: 'POST', body }),
      invalidatesTags: ['Sources'],
    }),

    /** Update an existing data-source connector. */
    updateSource: builder.mutation<
      ApiOkResponse<DataSourceConfigDto>,
      { id: string; dto: SaveDataSourceConfigDto }
    >({
      query: ({ id, dto }) => ({ url: `sources/${id}`, method: 'PUT', body: dto }),
      invalidatesTags: ['Sources'],
    }),

    /** Delete a saved data-source connector. */
    deleteSource: builder.mutation<ApiOkResponse<void>, string>({
      query: (id) => ({ url: `sources/${id}`, method: 'DELETE' }),
      invalidatesTags: ['Sources'],
    }),

    // ── Query endpoints ────────────────────────────────────────────────────

    /** FieldList — schema of the target table. */
    getSchema: builder.query<ApiOkResponse<FieldListResult>, SourceQueryParams>({
      query: ({ sourceId, tableName }) => ({
        url: 'schema',
        params: {
          ...(sourceId && { sourceId }),
          ...(tableName && { tableName }),
        },
      }),
    }),

    /** Summarize — GroupBy + Measures aggregation. Pass sessionId to query in-memory session data. */
    summarize: builder.mutation<
      ApiOkResponse<SummarizeResult>,
      SummarizeRequest & { sessionId?: string }
    >({
      query: ({ sessionId, ...body }) => ({
        url: sessionId ? `session/${sessionId}/summarize` : 'summarize',
        method: 'POST',
        body,
      }),
    }),

    /**
     * Exact row count of a database table (optionally filtered), from one aggregate query.
     * Session uploads don't need it: their details response already carries the total.
     */
    getRowCount: builder.query<number, RowCountQueryParams>({
      query: (params) => ({ url: 'summarize', method: 'POST', body: buildRowCountRequest(params) }),
      transformResponse: (res: ApiOkResponse<SummarizeResult>) => sumRowCounts(res.data),
    }),

    /** Details — paginated raw rows. Pass sessionId to query in-memory session data. */
    details: builder.mutation<
      ApiOkResponse<DetailsResult>,
      DetailsRequest & { sessionId?: string }
    >({
      query: ({ sessionId, ...body }) => ({
        url: sessionId ? `session/${sessionId}/details` : 'details',
        method: 'POST',
        body,
      }),
    }),

    /** MaxDate — MAX value of the chosen date field. Pass sessionId for session data. */
    getMaxDate: builder.query<
      ApiOkResponse<MaxDateResult>,
      MaxDateQueryParams & { sessionId?: string }
    >({
      query: ({ sessionId, sourceId, tableName, dateField }) => ({
        url: sessionId ? `session/${sessionId}/maxdate` : 'maxdate',
        params: {
          ...(dateField && { dateField }),
          ...(!sessionId && sourceId && { sourceId }),
          ...(!sessionId && tableName && { tableName }),
        },
      }),
    }),

    /** DistinctValues — unique values for a given column (filter LoV). Pass sessionId for session data. */
    getDistinctValues: builder.query<
      ApiOkResponse<DistinctValuesResult>,
      DistinctValuesQueryParams & { sessionId?: string }
    >({
      query: ({ sessionId, field, sourceId, tableName }) => ({
        url: sessionId ? `session/${sessionId}/distinctvalues` : 'distinctvalues',
        params: {
          field,
          ...(!sessionId && sourceId && { sourceId }),
          ...(!sessionId && tableName && { tableName }),
        },
      }),
    }),

    /** Upload tabular data into a temporary 2-hour in-memory session. */
    uploadSession: builder.mutation<ApiOkResponse<SessionUploadResponse>, SessionUploadRequest>({
      query: (body) => ({ url: 'session', method: 'POST', body }),
    }),

    /** NL Summarize — translates a free-text query to parameters via LLM, then executes. */
    nlSummarize: builder.mutation<ApiOkResponse<NlSummarizeResult>, NlSummarizeRequest>({
      query: (body) => ({ url: 'summarize/nl', method: 'POST', body }),
    }),
  }),
});

export const {
  useGetSourcesQuery,
  useCreateSourceMutation,
  useUpdateSourceMutation,
  useDeleteSourceMutation,
  useGetSchemaQuery,
  useSummarizeMutation,
  useGetRowCountQuery,
  useDetailsMutation,
  useGetMaxDateQuery,
  useGetDistinctValuesQuery,
  useUploadSessionMutation,
  useNlSummarizeMutation,
} = dataAnalysisApis;
