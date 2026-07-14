import { lazy } from 'react';
import { Route, Routes } from 'react-router-dom';

import RequireAuth from '../auth/RequireAuth';

const DailyChallenge = lazy(() => import('../DailyChallenge'));
const Dictionary = lazy(() => import('../Dictionary'));
const Exam = lazy(() => import('../Exam'));
const Repository = lazy(() => import('../Repository'));
const Review = lazy(() => import('../Review'));
const Stats = lazy(() => import('../Stats'));

/** Mounted at "/english/*" in App.tsx — groups every English Learning page under one prefix. */
function EnglishLearningRoutes() {
  return (
    <Routes>
      <Route path="dictionary" element={<Dictionary />} />
      <Route
        path="daily"
        element={
          <RequireAuth>
            <DailyChallenge />
          </RequireAuth>
        }
      />
      <Route
        path="review"
        element={
          <RequireAuth>
            <Review />
          </RequireAuth>
        }
      />
      <Route
        path="stats"
        element={
          <RequireAuth>
            <Stats />
          </RequireAuth>
        }
      />
      <Route
        path="repository"
        element={
          <RequireAuth>
            <Repository />
          </RequireAuth>
        }
      />
      <Route
        path="exam/*"
        element={
          <RequireAuth>
            <Exam />
          </RequireAuth>
        }
      />
    </Routes>
  );
}

export default EnglishLearningRoutes;
