import { configureStore } from '@reduxjs/toolkit';

import { accountApis } from '@/apis/accountApis';
import { connectorsApi } from '@/apis/connectorsApis/connectorsApis';
import { creditApis } from '@/apis/creditApis';
import { dailyChallengeApis } from '@/apis/dailyChallengeApis';
import { dataAnalysisApis } from '@/apis/dataAnalysisApis';
import { dictionaryApis } from '@/apis/dictionaryApis';
import { ecpayApi } from '@/apis/ecpayApis';
import { examApis } from '@/apis/examApis';
import { financeApis } from '@/apis/financeApis';
import { jobApis } from '@/apis/jobApis';
import { paypalApi } from '@/apis/paypalApis';
import { repositoryApis } from '@/apis/repositoryApis';
import { reviewApis } from '@/apis/reviewApis';
import { statsApis } from '@/apis/statsApis';
import { stripeApi } from '@/apis/stripeApis';
import { rtkQueryErrorLogger } from '@/redux/errorMiddleware';
import uiReducer from '@/redux/uiSlice';

export const store = configureStore({
  reducer: {
    ui: uiReducer,
    // Add the generated reducer as a specific top-level slice
    [dictionaryApis.reducerPath]: dictionaryApis.reducer,
    [repositoryApis.reducerPath]: repositoryApis.reducer,
    [accountApis.reducerPath]: accountApis.reducer,
    [examApis.reducerPath]: examApis.reducer,
    [financeApis.reducerPath]: financeApis.reducer,
    [jobApis.reducerPath]: jobApis.reducer,
    [connectorsApi.reducerPath]: connectorsApi.reducer,
    [creditApis.reducerPath]: creditApis.reducer,
    [stripeApi.reducerPath]: stripeApi.reducer,
    [paypalApi.reducerPath]: paypalApi.reducer,
    [ecpayApi.reducerPath]: ecpayApi.reducer,
    [dailyChallengeApis.reducerPath]: dailyChallengeApis.reducer,
    [reviewApis.reducerPath]: reviewApis.reducer,
    [statsApis.reducerPath]: statsApis.reducer,
    [dataAnalysisApis.reducerPath]: dataAnalysisApis.reducer,
  },
  // Adding the api middleware enables caching, invalidation, polling,
  // and other useful features of `rtk-query`.
  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware().concat(
      dictionaryApis.middleware,
      repositoryApis.middleware,
      accountApis.middleware,
      examApis.middleware,
      financeApis.middleware,
      jobApis.middleware,
      connectorsApi.middleware,
      creditApis.middleware,
      stripeApi.middleware,
      paypalApi.middleware,
      ecpayApi.middleware,
      dailyChallengeApis.middleware,
      reviewApis.middleware,
      statsApis.middleware,
      dataAnalysisApis.middleware,
      rtkQueryErrorLogger,
    ),
});

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof store.getState>;
// Inferred type: {posts: PostsState, comments: CommentsState, users: UsersState}
export type AppDispatch = typeof store.dispatch;
