import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';
import {
  DataSourceConfigDto,
  DetailsRequest,
  DetailsResult,
  DistinctValuesQueryParams,
  DistinctValuesResult,
  FieldListResult,
  MaxDateQueryParams,
  MaxDateResult,
  SaveDataSourceConfigDto,
  SourceQueryParams,
  SummarizeRequest,
  SummarizeResult,
} from './types';

export const dataAnalysisApis = createApi({
  reducerPath: 'dataAnalysisApis',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/dataanalysis' }),
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
          ...(sourceId   && { sourceId }),
          ...(tableName  && { tableName }),
        },
      }),
    }),

    /** Summarize — GroupBy + Measures aggregation. */
    summarize: builder.mutation<ApiOkResponse<SummarizeResult>, SummarizeRequest>({
      query: (body) => ({ url: 'summarize', method: 'POST', body }),
    }),

    /** Details — paginated raw rows. */
    details: builder.mutation<ApiOkResponse<DetailsResult>, DetailsRequest>({
      query: (body) => ({ url: 'details', method: 'POST', body }),
    }),

    /** MaxDate — MAX value of the chosen date field. */
    getMaxDate: builder.query<ApiOkResponse<MaxDateResult>, MaxDateQueryParams>({
      query: ({ sourceId, tableName, dateField }) => ({
        url: 'maxdate',
        params: {
          ...(sourceId   && { sourceId }),
          ...(tableName  && { tableName }),
          ...(dateField  && { dateField }),
        },
      }),
    }),

    /** DistinctValues — unique values for a given column (filter LoV). */
    getDistinctValues: builder.query<ApiOkResponse<DistinctValuesResult>, DistinctValuesQueryParams>({
      query: ({ field, sourceId, tableName }) => ({
        url: 'distinctvalues',
        params: {
          field,
          ...(sourceId   && { sourceId }),
          ...(tableName  && { tableName }),
        },
      }),
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
  useDetailsMutation,
  useGetMaxDateQuery,
  useGetDistinctValuesQuery,
} = dataAnalysisApis;
