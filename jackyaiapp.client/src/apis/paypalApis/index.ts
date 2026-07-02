import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';

import { CreditPack, PayPalCaptureResponse, PayPalOrderResponse } from './types';

export const paypalApi = createApi({
  reducerPath: 'paypalApi',
  baseQuery: apiBaseQuery('/api/paypal/', 'include'),
  endpoints: (builder) => ({
    getPayPalPacks: builder.query<CreditPack[], void>({ query: () => 'packs' }),
    createPayPalOrder: builder.mutation<PayPalOrderResponse, string>({
      query: (packId) => ({ url: 'create-order', method: 'POST', body: { packId } }),
    }),
    capturePayPalOrder: builder.mutation<PayPalCaptureResponse, string>({
      query: (orderId) => ({ url: 'capture-order', method: 'POST', body: { orderId } }),
    }),
  }),
});

export const {
  useGetPayPalPacksQuery,
  useCreatePayPalOrderMutation,
  useCapturePayPalOrderMutation,
} = paypalApi;
