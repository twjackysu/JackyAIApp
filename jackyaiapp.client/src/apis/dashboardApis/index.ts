import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';
import { ApiOkResponse } from '../types';

import type {
  AddWidgetRequest,
  DashboardDetail,
  DashboardSummary,
  DashboardVisibility,
  DashboardWidget,
  ShareSettings,
  WidgetLayout,
} from './types';
import type { SummarizeResult } from '../dataAnalysisApis/types';

export const dashboardApis = createApi({
  reducerPath: 'dashboardApis',
  baseQuery: apiBaseQuery('/api', 'include'),
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
    /**
     * Re-run a saved widget's plan to produce its data (auto-runs on view). Goes through the
     * dashboard-scoped endpoint so access is gated by the dashboard's visibility/shares and the
     * engine is resolved as the dashboard owner (works for shared viewers + custom sources).
     */
    getWidgetData: builder.query<
      ApiOkResponse<SummarizeResult>,
      { dashboardId: string; widgetId: string }
    >({
      query: ({ dashboardId, widgetId }) => `dashboard/${dashboardId}/widgets/${widgetId}/data`,
    }),
    renameDashboard: builder.mutation<ApiOkResponse<void>, { id: string; name: string }>({
      query: ({ id, name }) => ({ url: `dashboard/${id}`, method: 'PUT', body: { name } }),
      invalidatesTags: (_r, _e, { id }) => [{ type: 'Dashboard', id }, 'Dashboards'],
    }),
    deleteDashboard: builder.mutation<ApiOkResponse<void>, string>({
      query: (id) => ({ url: `dashboard/${id}`, method: 'DELETE' }),
      invalidatesTags: ['Dashboards'],
    }),
    updateLayout: builder.mutation<ApiOkResponse<void>, { id: string; items: WidgetLayout[] }>({
      query: ({ id, items }) => ({ url: `dashboard/${id}/layout`, method: 'PUT', body: { items } }),
      invalidatesTags: (_r, _e, { id }) => [{ type: 'Dashboard', id }],
    }),
    removeWidget: builder.mutation<ApiOkResponse<void>, { dashboardId: string; widgetId: string }>({
      query: ({ dashboardId, widgetId }) => ({
        url: `dashboard/${dashboardId}/widgets/${widgetId}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_r, _e, { dashboardId }) => [
        { type: 'Dashboard', id: dashboardId },
        'Dashboards',
      ],
    }),
    getShareSettings: builder.query<ApiOkResponse<ShareSettings>, string>({
      query: (id) => `dashboard/${id}/share`,
      providesTags: (_r, _e, id) => [{ type: 'Dashboard', id }],
    }),
    updateShareSettings: builder.mutation<
      ApiOkResponse<ShareSettings>,
      { id: string; visibility: DashboardVisibility; emails: string[] }
    >({
      query: ({ id, visibility, emails }) => ({
        url: `dashboard/${id}/share`,
        method: 'PUT',
        body: { visibility, emails },
      }),
      invalidatesTags: (_r, _e, { id }) => [{ type: 'Dashboard', id }, 'Dashboards'],
    }),
  }),
});

export const {
  useGetDashboardsQuery,
  useGetDashboardQuery,
  useCreateDashboardMutation,
  useAddWidgetMutation,
  useGetWidgetDataQuery,
  useRenameDashboardMutation,
  useDeleteDashboardMutation,
  useUpdateLayoutMutation,
  useRemoveWidgetMutation,
  useGetShareSettingsQuery,
  useUpdateShareSettingsMutation,
} = dashboardApis;
