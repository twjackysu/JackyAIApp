import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';
import {
  DetailsRequest,
  DetailsResult,
  DistinctValuesResult,
  FieldListResult,
  MaxDateResult,
  SummarizeRequest,
  SummarizeResult,
} from './types';

export const dataAnalysisApis = createApi({
  reducerPath: 'dataAnalysisApis',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/dataanalysis' }),
  endpoints: (builder) => ({
    /** FieldList — schema of dbo.Orders */
    getSchema: builder.query<ApiOkResponse<FieldListResult>, void>({
      query: () => 'schema',
    }),

    /** Summarize — GroupBy + Measures aggregation */
    summarize: builder.mutation<ApiOkResponse<SummarizeResult>, SummarizeRequest>({
      query: (body) => ({ url: 'summarize', method: 'POST', body }),
    }),

    /** Details — paginated raw rows */
    details: builder.mutation<ApiOkResponse<DetailsResult>, DetailsRequest>({
      query: (body) => ({ url: 'details', method: 'POST', body }),
    }),

    /** MaxDate — MAX(OrderDate) from dbo.Orders */
    getMaxDate: builder.query<ApiOkResponse<MaxDateResult>, void>({
      query: () => 'maxdate',
    }),

    /** DistinctValues — unique values for a given field (for filter LoV) */
    getDistinctValues: builder.query<ApiOkResponse<DistinctValuesResult>, string>({
      query: (field) => `distinctvalues?field=${encodeURIComponent(field)}`,
    }),
  }),
});

export const {
  useGetSchemaQuery,
  useSummarizeMutation,
  useDetailsMutation,
  useGetMaxDateQuery,
  useGetDistinctValuesQuery,
} = dataAnalysisApis;
