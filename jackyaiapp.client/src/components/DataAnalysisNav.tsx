import AssessmentIcon from '@mui/icons-material/Assessment';
import DashboardIcon from '@mui/icons-material/Dashboard';
import Box from '@mui/material/Box';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import { Link, useLocation } from 'react-router-dom';

/**
 * Quick switch between the Data Analysis workspace and Dashboards — they're
 * separate portal apps, so this lets users hop between them without returning
 * to the home catalog.
 */
export function DataAnalysisNav() {
  const { pathname } = useLocation();
  const active = pathname.startsWith('/dashboards') ? 'dashboards' : 'data';

  return (
    <Box sx={{ mb: 2 }}>
      <ToggleButtonGroup size="small" exclusive value={active} aria-label="section">
        <ToggleButton
          value="data"
          component={Link}
          to="/data-analysis"
          sx={{ textTransform: 'none' }}
        >
          <AssessmentIcon fontSize="small" sx={{ mr: 0.75 }} />
          Data Analysis
        </ToggleButton>
        <ToggleButton
          value="dashboards"
          component={Link}
          to="/dashboards"
          sx={{ textTransform: 'none' }}
        >
          <DashboardIcon fontSize="small" sx={{ mr: 0.75 }} />
          Dashboards
        </ToggleButton>
      </ToggleButtonGroup>
    </Box>
  );
}
