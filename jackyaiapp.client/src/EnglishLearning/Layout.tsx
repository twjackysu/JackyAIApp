import AutoStoriesIcon from '@mui/icons-material/AutoStories';
import BarChartIcon from '@mui/icons-material/BarChart';
import BookmarkIcon from '@mui/icons-material/Bookmark';
import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import PsychologyIcon from '@mui/icons-material/Psychology';
import QuizIcon from '@mui/icons-material/Quiz';
import Badge from '@mui/material/Badge';
import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import { Suspense } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';

import { useGetUserInfoQuery } from '../apis/accountApis';
import { useGetDueCountQuery } from '../apis/reviewApis';
import { ENGLISH_LEARNING } from '../constants/apps';

import CenteredSpinner from '@/components/CenteredSpinner';

const DICTIONARY_PATH_PREFIX = `${ENGLISH_LEARNING.path}/`;

/** Layout route for "/english" — renders the section's tab strip once, then an
 * <Outlet/> for whichever child page is active. Only the outlet content is wrapped
 * in its own Suspense so the tabs stay mounted (not swapped for a spinner) while a
 * not-yet-loaded page chunk comes in. */
function EnglishLearningLayout() {
  const location = useLocation();
  const path = location.pathname;

  const { data: userInfoRes } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;
  const { data: dueCountRes } = useGetDueCountQuery(undefined, { skip: !isLoggedIn });
  const dueCount = dueCountRes?.data?.dueCount ?? 0;

  // Bare "/english" renders Dictionary but isn't itself a Tab value, and a per-word
  // dictionary path ("/english/dictionary/apple") isn't the Tab's exact value either —
  // both fold back onto the Dictionary tab so the tab strip doesn't end up with nothing
  // selected.
  const currentTab =
    path === '/english' || path.startsWith(DICTIONARY_PATH_PREFIX) ? ENGLISH_LEARNING.path : path;

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
      <Suspense fallback={<CenteredSpinner sx={{ p: 4 }} />}>
        <Outlet />
      </Suspense>
    </>
  );
}

export default EnglishLearningLayout;
