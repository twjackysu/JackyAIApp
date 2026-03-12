import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

import { CheckoutResponse, CreditPack } from './types';

export const stripeApi = createApi({
  reducerPath: 'stripeApi',
  baseQuery: fetchBaseQuery({
    baseUrl: '/api/stripe/',
    credentials: 'include',
  }),
  endpoints: (builder) => ({
    getCreditPacks: builder.query<CreditPack[], void>({
      query: () => 'packs',
    }),
    createCheckout: builder.mutation<CheckoutResponse, string>({
      query: (packId) => ({
        url: 'checkout',
        method: 'POST',
        body: { packId },
      }),
    }),
  }),
});

export const { useGetCreditPacksQuery, useCreateCheckoutMutation } = stripeApi;
