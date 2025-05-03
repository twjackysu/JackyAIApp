import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';

import { Link, Route, Routes } from 'react-router-dom';
import Dictionary from './Dictionary';
import EffortPlanner from './EffortPlanner';
import Exam from './Exam';
import PDFUnlocker from './PDFUnlocker';
import Repository from './Repository';
import RequireAuth from './auth/RequireAuth';
import useRouteMatch from './hooks/useRouteMatch';

const tabRoutes = ['/', '/dictionary', '/repository', '/exam'];
function App() {
  const routeMatch = useRouteMatch(tabRoutes);
  const _currentTab = routeMatch?.pattern?.path;
  const currentTab = _currentTab === '/' ? '/dictionary' : _currentTab;
  return (
    <>
      {currentTab && (
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
            <Tab label="字典 (Dictionary)" value="/dictionary" to="/dictionary" component={Link} />
            <Tab
              label="儲存庫 (Repository)"
              value="/repository"
              to="/repository"
              component={Link}
            />
            <Tab label="考試 (Exam)" value="/exam" to="/exam" component={Link} />
          </Tabs>
        </Box>
      )}
      <Routes>
        <Route path="/pdf" element={<PDFUnlocker />} />
        <Route
          path="/effortPlanner"
          element={
            <RequireAuth>
              <EffortPlanner />
            </RequireAuth>
          }
        />
        <Route path="/dictionary" element={<Dictionary />} />
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
        <Route path="/" element={<Dictionary />} />
      </Routes>
    </>
  );
}

export default App;
