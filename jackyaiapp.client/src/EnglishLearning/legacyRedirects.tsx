// Route config (data), not a component — see index.tsx for why this rule doesn't apply here.
/* eslint-disable react-refresh/only-export-components */
import { Navigate, useLocation, useParams, type RouteObject } from 'react-router-dom';

/** Preserves query string/hash across the redirect (e.g. old "/dictionary?word=apple" links). */
function LegacyRedirect({ to }: { to: string }) {
  const location = useLocation();
  return <Navigate to={`${to}${location.search}${location.hash}`} replace />;
}

/** "/exam/listening" etc. needs the sub-path carried over, not just the "/exam" root. */
function ExamLegacyRedirect() {
  const { '*': splat } = useParams();
  const location = useLocation();
  const base = `/english/exam${splat ? `/${splat}` : ''}`;
  return <Navigate to={`${base}${location.search}${location.hash}`} replace />;
}

/** Pre-restructure top-level paths (before every English Learning page moved under
 * "/english") — redirects any bookmarked or shared link to its new location instead of
 * 404ing. */
export const legacyEnglishRedirects: RouteObject[] = [
  { path: '/dictionary', element: <LegacyRedirect to="/english/dictionary" /> },
  { path: '/daily', element: <LegacyRedirect to="/english/daily" /> },
  { path: '/repository', element: <LegacyRedirect to="/english/repository" /> },
  { path: '/review', element: <LegacyRedirect to="/english/review" /> },
  { path: '/stats', element: <LegacyRedirect to="/english/stats" /> },
  { path: '/exam/*', element: <ExamLegacyRedirect /> },
];

export default legacyEnglishRedirects;
