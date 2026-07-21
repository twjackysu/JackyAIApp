import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';

import { PayUniFormData, SubscriptionStatus } from './types';

export const payuniApi = createApi({
  reducerPath: 'payuniApi',
  baseQuery: apiBaseQuery('/api/payuni/', 'include'),
  tagTypes: ['SubscriptionStatus'],
  endpoints: (builder) => ({
    getSubscriptionStatus: builder.query<SubscriptionStatus, void>({
      query: () => 'subscription/status',
      providesTags: ['SubscriptionStatus'],
    }),
    createSubscription: builder.mutation<PayUniFormData, void>({
      query: () => ({ url: 'subscription/create', method: 'POST' }),
    }),
    cancelSubscription: builder.mutation<{ success: boolean }, void>({
      query: () => ({ url: 'subscription/cancel', method: 'POST' }),
      invalidatesTags: ['SubscriptionStatus'],
    }),
  }),
});

export const {
  useGetSubscriptionStatusQuery,
  useCreateSubscriptionMutation,
  useCancelSubscriptionMutation,
} = payuniApi;
