import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from '../baseQuery';

import { ECPayFormData, ECPayPack } from './types';

export const ecpayApi = createApi({
  reducerPath: 'ecpayApi',
  baseQuery: apiBaseQuery('/api/ecpay/', 'include'),
  endpoints: (builder) => ({
    getECPayPacks: builder.query<ECPayPack[], void>({ query: () => 'packs' }),
    createECPayPayment: builder.mutation<ECPayFormData, string>({
      query: (packId) => ({ url: 'create-payment', method: 'POST', body: { packId } }),
    }),
  }),
});

export const { useGetECPayPacksQuery, useCreateECPayPaymentMutation } = ecpayApi;
