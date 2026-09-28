import AppsIcon from '@mui/icons-material/Apps';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import { alpha } from '@mui/material/styles';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { lazy, Suspense } from 'react';
import { Helmet } from 'react-helmet-async';
import { useLocation, useRoutes, type RouteObject } from 'react-router-dom';

import RequireAuth from './auth/RequireAuth';
import Footer from './components/Footer';
import LoginError from './components/LoginError';
import InsufficientCreditsModal from './components/Modals/InsufficientCreditsModal';
import UserProfileMenu from './components/UserProfileMenu';
import {
  DATA_ANALYSIS,
  FINANCE,
  JOB_SEARCH,
  PDF_UNLOCKER,
  PORTFOLIO,
  SLIDESHOW,
  getCurrentApp,
} from './constants/apps';
import { englishLearningRoute } from './EnglishLearning';

import CenteredSpinner from '@/components/CenteredSpinner';

// Feature pages are code-split so each route loads its own chunk on demand.
const ListeningBankAdmin = lazy(() => import('./Admin/ListeningBank'));
const Connectors = lazy(() => import('./Connectors'));
const Credits = lazy(() => import('./Credits'));
const DataAnalysis = lazy(() => import('./DataAnalysis'));
const DashboardList = lazy(() => import('./Dashboard/DashboardList'));
const DashboardView = lazy(() => import('./Dashboard/DashboardView'));
const Finance = lazy(() => import('./Finance'));
const Portfolio = lazy(() => import('./Portfolio'));
const JobSearch = lazy(() => import('./JobSearch'));
const PDFUnlocker = lazy(() => import('./PDFUnlocker'));
const Portal = lazy(() => import('./Portal'));
const Pricing = lazy(() => import('./Pricing'));
const Slideshow = lazy(() => import('./Slideshow'));

// Floating AI chatbot — available on every page (auth-gated internally).
const FloatingChatbot = lazy(() => import('./components/FloatingChatbot'));

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
  {
    path: '/credits',
    element: (
      <RequireAuth>
        <Credits />
      </RequireAuth>
    ),
  },
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
  { path: '/login-error', element: <LoginError /> },
  // Public — ECPay merchant review requires a visible pricing/fee disclosure page
  // that doesn't require login to view.
  { path: '/pricing', element: <Pricing /> },
];

function App() {
  const location = useLocation();
  const path = location.pathname;
  const element = useRoutes(routes);

  // The home portal ("/") is the ONLY place users switch between apps.
  const isPortal = path === '/';
  const currentApp = getCurrentApp(path);

  // The app's accent colour is used only as a subtle tint behind the icon —
  // enough to give each site an identity without painting the whole bar.
  const accent = currentApp?.color ?? '#5E6AD2';

  return (
    <>
      {/* Site-level default title. A page further down the tree (e.g. the per-word
          Dictionary route) can render its own <Helmet> with a more specific <title> —
          react-helmet-async lets the deepest-rendered one win for singleton tags. */}
      <Helmet>
        <title>{currentApp?.name ?? "Jacky's Studio"}</title>
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
          <UserProfileMenu />
        </Toolbar>
      </AppBar>
      <Box sx={{ p: 2 }}>
        <Suspense fallback={<CenteredSpinner sx={{ p: 4 }} />}>{element}</Suspense>
      </Box>
      <Footer />
      {/* 懸浮 Chatbot — only on Finance: its Dify backend is wired to a TW-stock MCP server,
          so it can only answer stock questions and is out of place elsewhere. */}
      {currentApp?.name === FINANCE.name && (
        <Suspense fallback={null}>
          <FloatingChatbot />
        </Suspense>
      )}
      <InsufficientCreditsModal />
    </>
  );
}

export default App;
