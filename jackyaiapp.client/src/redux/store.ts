import { configureStore } from '@reduxjs/toolkit';

import { accountApis } from '@/apis/accountApis';
import { chatbotApis } from '@/apis/chatbotApis';
import { connectorsApi } from '@/apis/connectorsApis/connectorsApis';
import { creditApis } from '@/apis/creditApis';
import { dailyChallengeApis } from '@/apis/dailyChallengeApis';
import { dashboardApis } from '@/apis/dashboardApis';
import { dataAnalysisApis } from '@/apis/dataAnalysisApis';
import { dictionaryApis } from '@/apis/dictionaryApis';
import { ecpayApi } from '@/apis/ecpayApis';
import { examApis } from '@/apis/examApis';
import { financeApis } from '@/apis/financeApis';
import { jobApis } from '@/apis/jobApis';
import { paypalApi } from '@/apis/paypalApis';
import { payuniApi } from '@/apis/payuniApis';
import { portfolioApis } from '@/apis/portfolioApis';
import { repositoryApis } from '@/apis/repositoryApis';
import { reviewApis } from '@/apis/reviewApis';
import { statsApis } from '@/apis/statsApis';
import { rtkQueryErrorLogger } from '@/redux/errorMiddleware';
import uiReducer from '@/redux/uiSlice';

/**
 * Every RTK Query slice in the app, listed once.
 *
 * A slice needs both its reducer mounted and its middleware installed. Listing those
 * separately meant every new feature had to be added in two places, and forgetting the
 * middleware half fails *silently* — the hooks still fetch, but caching, invalidation,
 * polling and `refetchOnFocus` quietly stop working, with no error to notice. Deriving both
 * from one array makes that impossible.
 */
const apiSlices = [
  dictionaryApis,
  repositoryApis,
  accountApis,
  examApis,
  financeApis,
  jobApis,
  connectorsApi,
  creditApis,
  dashboardApis,
  paypalApi,
  ecpayApi,
  payuniApi,
  dailyChallengeApis,
  reviewApis,
  statsApis,
  dataAnalysisApis,
  chatbotApis,
  portfolioApis,
];

type ApiSlice = (typeof apiSlices)[number];

/**
 * `{ dictionaryApis: <its reducer>, repositoryApis: <its reducer>, ... }` — the same shape the
 * reducer map was written out by hand. `Object.fromEntries` alone would widen this to a string
 * index signature and take `RootState` down with it, so the keys are recovered from each
 * slice's literal `reducerPath` instead.
 */
type ApiReducers = { [Slice in ApiSlice as Slice['reducerPath']]: Slice['reducer'] };

const apiReducers = Object.fromEntries(
  apiSlices.map((api) => [api.reducerPath, api.reducer]),
) as ApiReducers;

export const store = configureStore({
  reducer: {
    ui: uiReducer,
    // Add the generated reducer as a specific top-level slice
    ...apiReducers,
  },
  // Adding the api middleware enables caching, invalidation, polling,
  // and other useful features of `rtk-query`.
  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware().concat(...apiSlices.map((api) => api.middleware), rtkQueryErrorLogger),
});

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof store.getState>;
// Inferred type: {posts: PostsState, comments: CommentsState, users: UsersState}
export type AppDispatch = typeof store.dispatch;
