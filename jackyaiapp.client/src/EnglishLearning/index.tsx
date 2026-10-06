// This file exports route config (data), not a component — Fast Refresh's component-only-export
// check doesn't apply to route config modules.
/* eslint-disable react-refresh/only-export-components */
import { lazy, type ReactNode } from 'react';
import { type RouteObject } from 'react-router-dom';

import LoginGate from '../auth/LoginGate';
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
  // Canonical per-word URL (Growth & Retention SEO — see ARCHITECTURE.md). Kept as a
  // separate route rather than an optional param since React Router doesn't support those.
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
      <RequireAuth
        fallback={
          <LoginGate
            title="你的個人單字庫"
            description="把查過的單字收進自己的單字庫，之後用複習與測驗把它們真正記起來。"
            bullets={['一鍵收藏字典查到的單字', '依照你的單字產生 AI 測驗', '追蹤學習進度']}
          />
        }
      >
        <Repository />
      </RequireAuth>
    ),
  },
  {
    path: 'exam/*',
    element: (
      <RequireAuth
        fallback={
          <LoginGate
            title="英文測驗"
            description="克漏字、翻譯、情境對話、聽力、閱讀與寫作，AI 即時批改。"
            bullets={[
              '多種測驗類型，從單字到對話',
              '優先針對你收藏的單字出題',
              'AI 即時批改與回饋',
            ]}
          />
        }
      >
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
