import AppsIcon from '@mui/icons-material/Apps';
import AutoStoriesIcon from '@mui/icons-material/AutoStories';
import BarChartIcon from '@mui/icons-material/BarChart';
import BookmarkIcon from '@mui/icons-material/Bookmark';
import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import PsychologyIcon from '@mui/icons-material/Psychology';
import QuizIcon from '@mui/icons-material/Quiz';
import AppBar from '@mui/material/AppBar';
import Badge from '@mui/material/Badge';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import { getContrastRatio } from '@mui/material/styles';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { useEffect, lazy, Suspense } from 'react';
import { Link, Route, Routes, useLocation } from 'react-router-dom';

import { useGetUserInfoQuery } from './apis/accountApis';
import { useGetDueCountQuery } from './apis/reviewApis';
import RequireAuth from './auth/RequireAuth';
import LoginError from './components/LoginError';
import InsufficientCreditsModal from './components/Modals/InsufficientCreditsModal';
import UserProfileMenu from './components/UserProfileMenu';
import {
  DATA_ANALYSIS,
  ENGLISH_LEARNING,
  FINANCE,
  JOB_SEARCH,
  PDF_UNLOCKER,
  getCurrentApp,
} from './constants/apps';

// Feature pages are code-split so each route loads its own chunk on demand.
const Connectors = lazy(() => import('./Connectors'));
const Credits = lazy(() => import('./Credits'));
const DailyChallenge = lazy(() => import('./DailyChallenge'));
const DataAnalysis = lazy(() => import('./DataAnalysis'));
const Dictionary = lazy(() => import('./Dictionary'));
const Exam = lazy(() => import('./Exam'));
const Finance = lazy(() => import('./Finance'));
const JobSearch = lazy(() => import('./JobSearch'));
const PDFUnlocker = lazy(() => import('./PDFUnlocker'));
const Portal = lazy(() => import('./Portal'));
const Repository = lazy(() => import('./Repository'));
const Review = lazy(() => import('./Review'));
const Stats = lazy(() => import('./Stats'));

// Floating AI chatbot — available on every page (auth-gated internally).
const FloatingChatbot = lazy(() => import('./components/FloatingChatbot'));

function App() {
  const location = useLocation();
  const path = location.pathname;

  // The home portal ("/") is the ONLY place users switch between apps.
  const isPortal = path === '/';
  const currentApp = getCurrentApp(path);

  const isEnglishLearningSection = currentApp?.name === ENGLISH_LEARNING.name;

  // Due-review count for the Review tab badge (pull, not push — only while signed in
  // and viewing the English-learning section).
  const { data: userInfoRes } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;
  const { data: dueCountRes } = useGetDueCountQuery(undefined, {
    skip: !isLoggedIn || !isEnglishLearningSection,
  });
  const dueCount = dueCountRes?.data?.dueCount ?? 0;

  const currentTab = isEnglishLearningSection ? path : ENGLISH_LEARNING.path;

  // Keep the document title in sync with the current site so each feels standalone.
  useEffect(() => {
    document.title = currentApp?.name ?? "Jacky's Studio";
  }, [currentApp]);

  // Pick readable text colour against the app's brand colour.
  const barColor = currentApp?.color;
  const onBarColor =
    barColor && getContrastRatio(barColor, '#fff') >= 3 ? '#fff' : 'rgba(0, 0, 0, 0.87)';

  return (
    <>
      <AppBar
        position="static"
        elevation={1}
        sx={barColor ? { bgcolor: barColor, color: onBarColor } : undefined}
      >
        <Toolbar variant="dense">
          <Box sx={{ flexGrow: 1, display: 'flex', alignItems: 'center', gap: 1 }}>
            {/* Brand label only — never a switcher. Apps are only reachable from "/". */}
            {!isPortal && (currentApp?.icon ?? <AppsIcon fontSize="small" />)}
            <Typography variant="h6" sx={{ fontWeight: 800, letterSpacing: 0.5 }}>
              {isPortal ? "Jacky's Studio" : (currentApp?.name ?? "Jacky's Studio")}
            </Typography>
          </Box>
          <UserProfileMenu />
        </Toolbar>
      </AppBar>
      {isEnglishLearningSection && (
        <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
          <Tabs
            value={currentTab}
            scrollButtons="auto"
            variant="scrollable"
            allowScrollButtonsMobile
            sx={{
              '& .MuiTabs-scrollButtons': {
                '&.Mui-disabled': { opacity: 0.3 },
              },
            }}
          >
            <Tab
              icon={<LocalFireDepartmentIcon />}
              iconPosition="start"
              label="每日挑戰 (Daily)"
              value="/daily"
              to="/daily"
              component={Link}
            />
            <Tab
              icon={<AutoStoriesIcon />}
              iconPosition="start"
              label="字典 (Dictionary)"
              value={ENGLISH_LEARNING.path}
              to={ENGLISH_LEARNING.path}
              component={Link}
            />
            <Tab
              icon={<BookmarkIcon />}
              iconPosition="start"
              label="儲存庫 (Repository)"
              value="/repository"
              to="/repository"
              component={Link}
            />
            <Tab
              icon={<QuizIcon />}
              iconPosition="start"
              label="考試 (Exam)"
              value="/exam"
              to="/exam"
              component={Link}
            />
            <Tab
              icon={
                <Badge
                  badgeContent={dueCount}
                  color="error"
                  max={99}
                  overlap="circular"
                  sx={{ '& .MuiBadge-badge': { right: -3, top: -2 } }}
                >
                  <PsychologyIcon />
                </Badge>
              }
              iconPosition="start"
              label="複習 (Review)"
              value="/review"
              to="/review"
              component={Link}
            />
            <Tab
              icon={<BarChartIcon />}
              iconPosition="start"
              label="進度 (Stats)"
              value="/stats"
              to="/stats"
              component={Link}
            />
          </Tabs>
        </Box>
      )}
      <Box sx={{ p: 2 }}>
        <Suspense
          fallback={
            <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
              <CircularProgress />
            </Box>
          }
        >
          <Routes>
            <Route path="/" element={<Portal />} />
            <Route path={PDF_UNLOCKER.path} element={<PDFUnlocker />} />
            <Route path={ENGLISH_LEARNING.path} element={<Dictionary />} />
            <Route
              path="/daily"
              element={
                <RequireAuth>
                  <DailyChallenge />
                </RequireAuth>
              }
            />
            <Route
              path="/review"
              element={
                <RequireAuth>
                  <Review />
                </RequireAuth>
              }
            />
            <Route
              path="/stats"
              element={
                <RequireAuth>
                  <Stats />
                </RequireAuth>
              }
            />
            <Route
              path="/repository"
              element={
                <RequireAuth>
                  <Repository />
                </RequireAuth>
              }
            />
            <Route
              path="/exam/*"
              element={
                <RequireAuth>
                  <Exam />
                </RequireAuth>
              }
            />
            <Route
              path="/connectors"
              element={
                <RequireAuth>
                  <Connectors />
                </RequireAuth>
              }
            />
            <Route
              path="/credits"
              element={
                <RequireAuth>
                  <Credits />
                </RequireAuth>
              }
            />
            <Route path={FINANCE.path} element={<Finance />} />
            <Route path={JOB_SEARCH.path} element={<JobSearch />} />
            <Route path={DATA_ANALYSIS.path} element={<DataAnalysis />} />
            <Route path="/login-error" element={<LoginError />} />
          </Routes>
        </Suspense>
      </Box>
      {/* 懸浮 Chatbot — available on every page (auth-gated internally) */}
      <Suspense fallback={null}>
        <FloatingChatbot />
      </Suspense>
      <InsufficientCreditsModal />
    </>
  );
}

export default App;
