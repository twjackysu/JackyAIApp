import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';

import { ECPayFormData, ECPayPack, SubscriptionStatus } from './types';

export const ecpayApi = createApi({
  reducerPath: 'ecpayApi',
  baseQuery: apiBaseQuery('/api/ecpay/', 'include'),
  tagTypes: ['SubscriptionStatus'],
  endpoints: (builder) => ({
    getECPayPacks: builder.query<ECPayPack[], void>({ query: () => 'packs' }),
    createECPayPayment: builder.mutation<ECPayFormData, string>({
      query: (packId) => ({ url: 'create-payment', method: 'POST', body: { packId } }),
    }),
    getSubscriptionStatus: builder.query<SubscriptionStatus, void>({
      query: () => 'subscription/status',
      providesTags: ['SubscriptionStatus'],
    }),
    createSubscription: builder.mutation<ECPayFormData, void>({
      query: () => ({ url: 'subscription/create', method: 'POST' }),
    }),
    cancelSubscription: builder.mutation<{ success: boolean }, void>({
      query: () => ({ url: 'subscription/cancel', method: 'POST' }),
      invalidatesTags: ['SubscriptionStatus'],
    }),
  }),
});

export const {
  useGetECPayPacksQuery,
  useCreateECPayPaymentMutation,
  useGetSubscriptionStatusQuery,
  useCreateSubscriptionMutation,
  useCancelSubscriptionMutation,
} = ecpayApi;
