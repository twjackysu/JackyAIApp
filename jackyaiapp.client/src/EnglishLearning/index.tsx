// This file exports route config (data), not a component — Fast Refresh's component-only-export
// check doesn't apply to route config modules.
/* eslint-disable react-refresh/only-export-components */
import { lazy, type ReactNode } from 'react';
import { type RouteObject } from 'react-router-dom';

import RequireAuth from '../auth/RequireAuth';

import EnglishLearningLayout from './Layout';

const DailyChallenge = lazy(() => import('../DailyChallenge'));
const Dictionary = lazy(() => import('../Dictionary'));
const Exam = lazy(() => import('../Exam'));
const Repository = lazy(() => import('../Repository'));
const Review = lazy(() => import('../Review'));
const Stats = lazy(() => import('../Stats'));

interface EnglishChildRoute {
  /** Path relative to "/english". */
  path: string;
  element: ReactNode;
}

const children: EnglishChildRoute[] = [
  { path: 'dictionary', element: <Dictionary /> },
  { path: 'dictionary/:word', element: <Dictionary /> },
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
];

/** Canonical route: "/english" layout with every page nested under it via <Outlet/>. */
export const englishLearningRoute: RouteObject = {
  path: '/english',
  element: <EnglishLearningLayout />,
  children: [
    { index: true, element: <Dictionary /> },
    ...children.map(({ path, element }) => ({ path, element })),
  ],
};

export default englishLearningRoute;
