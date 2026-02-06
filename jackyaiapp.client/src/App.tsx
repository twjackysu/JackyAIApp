import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { useState, useEffect } from 'react';
import { Link, Route, Routes, useLocation } from 'react-router-dom';

import RequireAuth from './auth/RequireAuth';
import FloatingChatbot from './components/FloatingChatbot';
import LoginError from './components/LoginError';
import UserProfileMenu from './components/UserProfileMenu';
import Connectors from './Connectors';
import { apps, FINANCE, PDF_UNLOCKER, ENGLISH_LEARNING } from './constants/apps';
import Dictionary from './Dictionary';
import Exam from './Exam';
import Finance from './Finance';
import useRouteMatch from './hooks/useRouteMatch';
import PDFUnlocker from './PDFUnlocker';
import Repository from './Repository';

// Helper function to determine current app section based on route
const getCurrentAppSection = (path: string): string => {
  if (path === '/' || path === FINANCE.path) return FINANCE.name;
  if (path === PDF_UNLOCKER.path) return PDF_UNLOCKER.name;
  if (ENGLISH_LEARNING.allPaths.some((route) => path.startsWith(route)))
    return ENGLISH_LEARNING.name;
  return FINANCE.name;
};

function App() {
  const [appMenuAnchorEl, setAppMenuAnchorEl] = useState<null | HTMLElement>(null);
  const appMenuOpen = Boolean(appMenuAnchorEl);
  const location = useLocation();

  const routeMatch = useRouteMatch(
    FINANCE.allPaths
      .concat(PDF_UNLOCKER.allPaths)
      .concat(ENGLISH_LEARNING.allPaths),
  );
  const _currentTab = routeMatch?.pattern?.path || '/';
  const currentTab = _currentTab === '/' ? FINANCE.path : _currentTab;

  // Determine if we're in the English Learning section
  const isEnglishLearningSection = ENGLISH_LEARNING.allPaths.some((path) =>
    currentTab.startsWith(path),
  );

  // Get current section title for the app bar
  const currentAppSection = getCurrentAppSection(currentTab);

  // Get current app object
  const currentApp = apps.find(
    (app) =>
      app.name === currentAppSection ||
      (app.name === ENGLISH_LEARNING.name && isEnglishLearningSection),
  );

  const handleAppMenuOpen = (event: React.MouseEvent<HTMLElement>) => {
    setAppMenuAnchorEl(event.currentTarget);
  };

  const handleAppMenuClose = () => {
    setAppMenuAnchorEl(null);
  };

  // Update document title when route changes
  useEffect(() => {
    const appSection = getCurrentAppSection(currentTab);
    document.title = appSection;
  }, [currentTab, location.pathname]);

  return (
    <>
      <AppBar position="static" color="primary" elevation={1}>
        <Toolbar variant="dense">
          <Box
            sx={{
              flexGrow: 1,
              display: 'flex',
              alignItems: 'center',
            }}
          >
            <Button
              id="app-switcher-button"
              aria-controls={appMenuOpen ? 'app-switcher-menu' : undefined}
              aria-haspopup="true"
              aria-expanded={appMenuOpen ? 'true' : undefined}
              onClick={handleAppMenuOpen}
              color="inherit"
              sx={{
                textTransform: 'none',
                fontWeight: 'bold',
                fontSize: 'h6.fontSize',
              }}
              startIcon={currentApp?.icon}
              endIcon={<KeyboardArrowDownIcon />}
            >
              {currentAppSection}
            </Button>
            <Menu
              id="app-switcher-menu"
              anchorEl={appMenuAnchorEl}
              open={appMenuOpen}
              onClose={handleAppMenuClose}
              MenuListProps={{
                'aria-labelledby': 'app-switcher-button',
              }}
            >
              {apps.map((app) => (
                <MenuItem
                  key={app.path}
                  component={Link}
                  to={app.path}
                  onClick={handleAppMenuClose}
                  selected={
                    app.name === currentAppSection ||
                    (app.name === ENGLISH_LEARNING.name && isEnglishLearningSection)
                  }
                  sx={{
                    minWidth: 200,
                    '&:hover': { backgroundColor: 'action.hover' },
                  }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'center' }}>
                    {app.icon}
                    <Typography variant="body1" sx={{ ml: 1 }}>
                      {app.name}
                    </Typography>
                  </Box>
                </MenuItem>
              ))}
            </Menu>
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
              label="字典 (Dictionary)"
              value={ENGLISH_LEARNING.path}
              to={ENGLISH_LEARNING.path}
              component={Link}
            />
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

      <Box sx={{ p: 2 }}>
        <Routes>
          <Route path={PDF_UNLOCKER.path} element={<PDFUnlocker />} />
          <Route path={ENGLISH_LEARNING.path} element={<Dictionary />} />
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
          <Route path={FINANCE.path} element={<Finance />} />
          <Route path="/login-error" element={<LoginError />} />
          <Route path="/" element={<Finance />} />
        </Routes>
      </Box>

      {/* 懸浮 Chatbot */}
      <RequireAuth>
        <FloatingChatbot />
      </RequireAuth>
    </>
  );
}

export default App;
