import type { Dispatch } from '@reduxjs/toolkit';

/** Marks another slice's cached data stale, e.g. `refreshReviewQueue` from `reviewApis`. */
export type SliceRefresh = (dispatch: Dispatch) => unknown;

/**
 * An `onQueryStarted` for a mutation whose server-side effect shows up in *other* slices' data.
 *
 * Every RTK Query slice here is its own `createApi`, so a tag one slice invalidates never reaches
 * another: adding a word only invalidated `repositoryApis`, and the review queue's due count went
 * on showing the old number. Once the mutation succeeds, this refreshes each slice it is given;
 * a failed mutation changed nothing, so nothing is refreshed.
 */
export const alsoRefresh =
  (...targets: SliceRefresh[]) =>
  async (
    _arg: unknown,
    { dispatch, queryFulfilled }: { dispatch: Dispatch; queryFulfilled: Promise<unknown> },
  ) => {
    try {
      await queryFulfilled;
    } catch {
      return;
    }
    targets.forEach((refresh) => refresh(dispatch));
  };
