import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { ApiOkResponse } from '../types';

import type { AddWidgetRequest, DashboardDetail, DashboardSummary, DashboardWidget } from './types';
import type { SummarizeRequest, SummarizeResult } from '../dataAnalysisApis/types';

export const dashboardApis = createApi({
  reducerPath: 'dashboardApis',
  baseQuery: fetchBaseQuery({ baseUrl: '/api', credentials: 'include' }),
  tagTypes: ['Dashboards', 'Dashboard'],
  endpoints: (builder) => ({
    getDashboards: builder.query<ApiOkResponse<DashboardSummary[]>, void>({
      query: () => 'dashboard',
      providesTags: ['Dashboards'],
    }),
    getDashboard: builder.query<ApiOkResponse<DashboardDetail>, string>({
      query: (id) => `dashboard/${id}`,
      providesTags: (_r, _e, id) => [{ type: 'Dashboard', id }],
    }),
    createDashboard: builder.mutation<ApiOkResponse<DashboardSummary>, { name: string }>({
      query: (body) => ({ url: 'dashboard', method: 'POST', body }),
      invalidatesTags: ['Dashboards'],
    }),
    addWidget: builder.mutation<
      ApiOkResponse<DashboardWidget>,
      { dashboardId: string; widget: AddWidgetRequest }
    >({
      query: ({ dashboardId, widget }) => ({
        url: `dashboard/${dashboardId}/widgets`,
        method: 'POST',
        body: widget,
      }),
      invalidatesTags: (_r, _e, { dashboardId }) => [
        { type: 'Dashboard', id: dashboardId },
        'Dashboards',
      ],
    }),
    /** Re-run a saved widget's plan to produce its data (auto-runs on view). */
    runWidget: builder.query<ApiOkResponse<SummarizeResult>, SummarizeRequest>({
      query: (plan) => ({ url: 'dataanalysis/summarize', method: 'POST', body: plan }),
    }),
  }),
});

export const {
  useGetDashboardsQuery,
  useGetDashboardQuery,
  useCreateDashboardMutation,
  useAddWidgetMutation,
  useRunWidgetQuery,
} = dashboardApis;
