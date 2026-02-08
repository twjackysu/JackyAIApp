import { configureStore } from '@reduxjs/toolkit';

import { accountApis } from '@/apis/accountApis';
import { connectorsApi } from '@/apis/connectorsApis/connectorsApis';
import { creditApis } from '@/apis/creditApis';
import { dictionaryApis } from '@/apis/dictionaryApis';
import { examApis } from '@/apis/examApis';
import { financeApis } from '@/apis/financeApis';
import { repositoryApis } from '@/apis/repositoryApis';

export const store = configureStore({
  reducer: {
    // Add the generated reducer as a specific top-level slice
    [dictionaryApis.reducerPath]: dictionaryApis.reducer,
    [repositoryApis.reducerPath]: repositoryApis.reducer,
    [accountApis.reducerPath]: accountApis.reducer,
    [examApis.reducerPath]: examApis.reducer,
    [financeApis.reducerPath]: financeApis.reducer,
    [connectorsApi.reducerPath]: connectorsApi.reducer,
    [creditApis.reducerPath]: creditApis.reducer,
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
      connectorsApi.middleware,
      creditApis.middleware,
    ),
});

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof store.getState>;
// Inferred type: {posts: PostsState, comments: CommentsState, users: UsersState}
export type AppDispatch = typeof store.dispatch;
