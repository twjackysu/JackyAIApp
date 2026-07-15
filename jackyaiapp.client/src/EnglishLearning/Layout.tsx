import AutoStoriesIcon from '@mui/icons-material/AutoStories';
import BarChartIcon from '@mui/icons-material/BarChart';
import BookmarkIcon from '@mui/icons-material/Bookmark';
import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import PsychologyIcon from '@mui/icons-material/Psychology';
import QuizIcon from '@mui/icons-material/Quiz';
import Badge from '@mui/material/Badge';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import { Suspense, type ReactNode } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';

import { useGetUserInfoQuery } from '../apis/accountApis';
import { useGetDueCountQuery } from '../apis/reviewApis';
import { ENGLISH_LEARNING } from '../constants/apps';

interface EnglishLearningLayoutProps {
  /** Passed only by the pre-restructure legacy mounts (e.g. "/dictionary" directly, no
   * "/english" prefix) — those aren't nested under the "/english" parent route, so there's
   * no <Outlet/> match to render; the page element is given directly instead. Canonical
   * "/english/*" routes render via <Outlet/> as usual (no children passed). */
  children?: ReactNode;
}

/** Layout for the English Learning tab strip, shared by both the canonical "/english/*"
 * routes (via <Outlet/>) and the legacy pre-restructure top-level paths (via children) so
 * old bookmarked/shared links keep rendering the same page at the same URL — no redirect. */
function EnglishLearningLayout({ children }: EnglishLearningLayoutProps) {
  const location = useLocation();
  const path = location.pathname;

  const { data: userInfoRes } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;
  const { data: dueCountRes } = useGetDueCountQuery(undefined, { skip: !isLoggedIn });
  const dueCount = dueCountRes?.data?.dueCount ?? 0;

  // Tab values are all "/english/xxx". Normalize legacy paths (e.g. "/dictionary") and the
  // bare "/english" landing page into that same space so the right tab still highlights
  // regardless of which URL variant got the user here.
  const normalizedPath = path.startsWith('/english') ? path : `/english${path}`;
  const currentTab = normalizedPath === '/english' ? ENGLISH_LEARNING.path : normalizedPath;

  return (
    <>
      {/* Negative margin cancels the parent <Box sx={{ p: 2 }}> in App.tsx so the tab
          strip stays edge-to-edge under the app bar, matching the pre-refactor look. */}
      <Box sx={{ borderBottom: 1, borderColor: 'divider', mx: -2, mt: -2, mb: 2 }}>
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
            value="/english/daily"
            to="/english/daily"
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
            value="/english/repository"
            to="/english/repository"
            component={Link}
          />
          <Tab
            icon={<QuizIcon />}
            iconPosition="start"
            label="考試 (Exam)"
            value="/english/exam"
            to="/english/exam"
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
            value="/english/review"
            to="/english/review"
            component={Link}
          />
          <Tab
            icon={<BarChartIcon />}
            iconPosition="start"
            label="進度 (Stats)"
            value="/english/stats"
            to="/english/stats"
            component={Link}
          />
        </Tabs>
      </Box>
      <Suspense
        fallback={
          <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
            <CircularProgress />
          </Box>
        }
      >
        {children ?? <Outlet />}
      </Suspense>
    </>
  );
}

export default EnglishLearningLayout;
