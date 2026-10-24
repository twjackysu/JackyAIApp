import AppsIcon from '@mui/icons-material/Apps';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import { alpha } from '@mui/material/styles';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { lazy, Suspense } from 'react';
import { Helmet } from 'react-helmet-async';
import { Navigate, useLocation, useRoutes, type RouteObject } from 'react-router-dom';

import RequireAuth from './auth/RequireAuth';
import LoginError from './components/LoginError';
import UsageLimitModal from './components/Modals/UsageLimitModal';
import StudioAssistant from './components/StudioAssistant';
import UserProfileMenu from './components/UserProfileMenu';
import {
  DATA_ANALYSIS,
  FINANCE,
  JOB_SEARCH,
  PDF_UNLOCKER,
  PORTFOLIO,
  SITE_HOME_TITLE,
  SITE_NAME,
  SLIDESHOW,
  getCurrentApp,
} from './constants/apps';
import { englishLearningRoute } from './EnglishLearning';
import usePageViewTracking from './hooks/usePageViewTracking';

import CenteredSpinner from '@/components/CenteredSpinner';

// Feature pages are code-split so each route loads its own chunk on demand.
const ListeningBankAdmin = lazy(() => import('./Admin/ListeningBank'));
const Connectors = lazy(() => import('./Connectors'));
const DataAnalysis = lazy(() => import('./DataAnalysis'));
const DashboardList = lazy(() => import('./Dashboard/DashboardList'));
const DashboardView = lazy(() => import('./Dashboard/DashboardView'));
const Finance = lazy(() => import('./Finance'));
const Portfolio = lazy(() => import('./Portfolio'));
const JobSearch = lazy(() => import('./JobSearch'));
const PDFUnlocker = lazy(() => import('./PDFUnlocker'));
const OAuthConsent = lazy(() => import('./OAuthConsent'));
const Portal = lazy(() => import('./Portal'));
const Slideshow = lazy(() => import('./Slideshow'));

// Route config as plain objects (not JSX <Routes>) so englishLearningRoute's real
// parent/child nesting with <Outlet/> can be composed in from a separate file — the
// JSX <Routes> API only recognizes literal <Route> children in its own tree.
const routes: RouteObject[] = [
  { path: '/', element: <Portal /> },
  { path: PDF_UNLOCKER.path, element: <PDFUnlocker /> },
  // No RequireAuth: everything happens in the browser against a folder the user picks —
  // there is no API call and no server-side state to protect.
  { path: SLIDESHOW.path, element: <Slideshow /> },
  englishLearningRoute,
  {
    path: '/connectors',
    element: (
      <RequireAuth>
        <Connectors />
      </RequireAuth>
    ),
  },
  // Credit/payment UI was removed (DECISION-007) — redirect old links to the portal
  // instead of 404ing.
  { path: '/credits', element: <Navigate to="/" replace /> },
  { path: '/pricing', element: <Navigate to="/" replace /> },
  {
    path: PORTFOLIO.path,
    element: (
      <RequireAuth>
        <Portfolio />
      </RequireAuth>
    ),
  },
  { path: FINANCE.path, element: <Finance /> },
  { path: JOB_SEARCH.path, element: <JobSearch /> },
  { path: DATA_ANALYSIS.path, element: <DataAnalysis /> },
  {
    path: '/dashboards',
    element: (
      <RequireAuth>
        <DashboardList />
      </RequireAuth>
    ),
  },
  // No RequireAuth: "Anyone"-visibility dashboards are viewable without login.
  // Other visibilities still return NotFound from the API for anonymous users.
  { path: '/dashboards/:id', element: <DashboardView /> },
  {
    path: '/admin/listening',
    element: (
      <RequireAuth>
        <ListeningBankAdmin />
      </RequireAuth>
    ),
  },
  // Another AI asking to use the site's MCP server (its OAuth sign-in): the page asks a signed-out
  // visitor to sign in itself, keeping the request's query, which RequireAuth would drop.
  { path: '/oauth/authorize', element: <OAuthConsent /> },
  { path: '/login-error', element: <LoginError /> },
];

function App() {
  const location = useLocation();
  const path = location.pathname;
  const element = useRoutes(routes);
  usePageViewTracking();

  // The home portal ("/") is the ONLY place users switch between apps.
  const isPortal = path === '/';
  const currentApp = getCurrentApp(path);
  // Tools with nothing tied to an account (see AppDef.standalone): the account menu is just
  // noise on them.
  const isStandalone = currentApp?.standalone === true;

  // The app's accent colour is used only as a subtle tint behind the icon —
  // enough to give each site an identity without painting the whole bar.
  const accent = currentApp?.color ?? '#5E6AD2';

  return (
    <>
      {/* Site-level default title. A page further down the tree (e.g. the per-word
          Dictionary route) can render its own <Helmet> with a more specific <title> —
          react-helmet-async lets the deepest-rendered one win for singleton tags. */}
      <Helmet>
        <title>{currentApp ? `${currentApp.name} | ${SITE_NAME}` : SITE_HOME_TITLE}</title>
      </Helmet>
      <AppBar
        position="static"
        elevation={0}
        color="transparent"
        sx={{
          borderBottom: '1px solid',
          borderColor: 'divider',
          backgroundColor: alpha('#0B0B0D', 0.6),
          backdropFilter: 'blur(8px)',
        }}
      >
        <Toolbar variant="dense">
          <Box sx={{ flexGrow: 1, display: 'flex', alignItems: 'center', gap: 1.25 }}>
            {/* Brand label only — never a switcher. Apps are only reachable from "/". */}
            {!isPortal && (
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  width: 28,
                  height: 28,
                  borderRadius: 1.5,
                  color: accent,
                  bgcolor: alpha(accent, 0.14),
                  '& svg': { fontSize: 18 },
                }}
              >
                {currentApp?.icon ?? <AppsIcon fontSize="small" />}
              </Box>
            )}
            <Typography variant="subtitle1" sx={{ fontWeight: 700, letterSpacing: 0.2 }}>
              {isPortal ? "Jacky's Studio" : (currentApp?.name ?? "Jacky's Studio")}
            </Typography>
          </Box>
          {!isStandalone && <UserProfileMenu />}
        </Toolbar>
      </AppBar>
      <Box sx={{ p: 2 }}>
        <Suspense fallback={<CenteredSpinner sx={{ p: 4 }} />}>{element}</Suspense>
      </Box>
      <Typography
        component="footer"
        variant="caption"
        color="text.secondary"
        sx={{ display: 'block', textAlign: 'center', px: 2, pb: 2 }}
      >
        本站使用匿名統計（Cookie）以改善服務
      </Typography>
      <UsageLimitModal />
      <StudioAssistant />
    </>
  );
}

export default App;
