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
  /** Path relative to "/english" — also doubles as the legacy top-level path (with a
   * leading "/") since every old path ("/dictionary", "/exam", ...) matches its new
   * "/english/xxx" counterpart 1:1. */
  path: string;
  element: ReactNode;
}

// Single source of truth for every English Learning page's path + element, so the
// canonical "/english/*" tree and the legacy top-level mounts below can't drift apart —
// each page is defined exactly once.
const children: EnglishChildRoute[] = [
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

/** Pre-restructure top-level paths (before every English Learning page moved under
 * "/english") — each still renders the same page at the same old URL, no redirect, so any
 * bookmarked or shared link keeps working. Mechanically derived from `children` above so
 * there's exactly one definition per page, not a hand-kept second copy. */
export const legacyEnglishLearningRoutes: RouteObject[] = children.map(({ path, element }) => ({
  path: `/${path}`,
  element: <EnglishLearningLayout>{element}</EnglishLearningLayout>,
}));

export default englishLearningRoute;
