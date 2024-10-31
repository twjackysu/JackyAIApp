import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';

import Dictionary from './Dictionary';
import Repository from './Repository';
import Exam from './Exam';
import useRouteMatch from './hooks/useRouteMatch';
import { Link, Route, Routes } from 'react-router-dom';
import RequireAuth from './auth/RequireAuth';
import PDFUnlocker from './PDFUnlocker';

const tabRoutes = ['/dictionary', '/repository', '/exam', '/'];
function App() {
  const routeMatch = useRouteMatch(tabRoutes);
  const currentTab = routeMatch?.pattern?.path;
  return (
    <>
      {currentTab && (
        <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
          <Tabs value={currentTab}>
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
          path="/exam"
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
