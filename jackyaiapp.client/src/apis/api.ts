import { createApi } from '@reduxjs/toolkit/query/react';

import { apiBaseQuery } from './baseQuery';

import type { TagDescription } from '@reduxjs/toolkit/query/react';

/**
 * Every tag the shared {@link api} knows. Listed together because the point is that they are
 * shared: a mutation whose server-side effect shows up in another feature's data names that
 * data's tag, and every query providing it refetches, whichever feature it belongs to.
 */
const TAG_TYPES = [
  'PersonalWord',
  'DueReviews',
  'DueCount',
  // The weekly report, leaderboard, activity heatmap and the daily challenge's streak/XP card:
  // words, reviews, challenges and exams each feed more than one of them.
  'LearningStats',
  'DailyChallenge',
  'ListeningBank',
] as const;

export type ApiTag = TagDescription<(typeof TAG_TYPES)[number]>;

/**
 * The RTK Query API that feature slices inject their endpoints into (`api.injectEndpoints`),
 * with paths relative to `/api`. One API rather than one per feature, so a tag one feature
 * invalidates reaches the others: separate `createApi`s each keep their own tags, and a change
 * one of them made never showed in another (adding a word left the review queue's due count
 * stale).
 *
 * The English-learning slices are here; the others are still their own `createApi`
 * (docs/改善計畫.md, Phase 3).
 */
export const api = createApi({
  reducerPath: 'api',
  baseQuery: apiBaseQuery('/api'),
  tagTypes: TAG_TYPES,
  endpoints: () => ({}),
});

/**
 * `invalidatesTags` for a mutation, applied only once it succeeded. RTK Query also invalidates
 * the tags of a mutation the server answered with an error, which changed nothing, so its
 * refetches would only cost requests.
 */
export const onSuccess =
  (...tags: ApiTag[]) =>
  (_result: unknown, error: unknown): ApiTag[] =>
    error ? [] : tags;
