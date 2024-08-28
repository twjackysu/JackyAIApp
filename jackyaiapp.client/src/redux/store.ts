import { configureStore } from '@reduxjs/toolkit';
import { dictionaryApis } from '@/apis/dictionaryApis';
import { repositoryApis } from '@/apis/repositoryApis';
import { accountApis } from '@/apis/accountApis';
import { examApis } from '@/apis/examApis';

export const store = configureStore({
  reducer: {
    // Add the generated reducer as a specific top-level slice
    [dictionaryApis.reducerPath]: dictionaryApis.reducer,
    [repositoryApis.reducerPath]: repositoryApis.reducer,
    [accountApis.reducerPath]: accountApis.reducer,
    [examApis.reducerPath]: examApis.reducer,
  },
  // Adding the api middleware enables caching, invalidation, polling,
  // and other useful features of `rtk-query`.
  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware().concat(
      dictionaryApis.middleware,
      repositoryApis.middleware,
      accountApis.middleware,
      examApis.middleware,
    ),
});

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof store.getState>;
// Inferred type: {posts: PostsState, comments: CommentsState, users: UsersState}
export type AppDispatch = typeof store.dispatch;
