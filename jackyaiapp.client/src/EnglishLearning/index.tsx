// This file exports route config (data), not a component — Fast Refresh's component-only-export
// check doesn't apply to route config modules.
/* eslint-disable react-refresh/only-export-components */
import { lazy } from 'react';
import { type RouteObject } from 'react-router-dom';

import RequireAuth from '../auth/RequireAuth';

import EnglishLearningLayout from './Layout';

const DailyChallenge = lazy(() => import('../DailyChallenge'));
const Dictionary = lazy(() => import('../Dictionary'));
const Exam = lazy(() => import('../Exam'));
const Repository = lazy(() => import('../Repository'));
const Review = lazy(() => import('../Review'));
const Stats = lazy(() => import('../Stats'));

/** Route config for the English Learning section, spread into App.tsx's useRoutes() array.
 * A plain object (not JSX) so it can be composed across files — react-router-dom's JSX
 * <Routes> only recognizes literal <Route> children in its own tree, so real parent/child
 * nesting with <Outlet/> across separate files requires the object-based route API. */
export const englishLearningRoute: RouteObject = {
  path: '/english',
  element: <EnglishLearningLayout />,
  children: [
    { index: true, element: <Dictionary /> },
    { path: 'dictionary', element: <Dictionary /> },
    {
      path: 'daily',
      element: (
        <RequireAuth>
          <DailyChallenge />
        </RequireAuth>
      ),
    },
    {
      path: 'review',
      element: (
        <RequireAuth>
          <Review />
        </RequireAuth>
      ),
    },
    {
      path: 'stats',
      element: (
        <RequireAuth>
          <Stats />
        </RequireAuth>
      ),
    },
    {
      path: 'repository',
      element: (
        <RequireAuth>
          <Repository />
        </RequireAuth>
      ),
    },
    {
      path: 'exam/*',
      element: (
        <RequireAuth>
          <Exam />
        </RequireAuth>
      ),
    },
  ],
};

export default englishLearningRoute;
