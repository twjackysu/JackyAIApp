import DashboardIcon from '@mui/icons-material/Dashboard';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import CircularProgress from '@mui/material/CircularProgress';
import { ThemeProvider } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { Link } from 'react-router-dom';

import { useGetDashboardsQuery } from '@/apis/dashboardApis';
import { dataAnalysisTheme } from '@/DataAnalysis/theme';

export default function DashboardList() {
  const { data, isLoading, isError } = useGetDashboardsQuery();
  const dashboards = data?.data ?? [];

  return (
    <ThemeProvider theme={dataAnalysisTheme}>
      <Box sx={{ maxWidth: 900, mx: 'auto' }}>
        <Typography variant="h5" sx={{ fontWeight: 700, mb: 2 }}>
          My Dashboards
        </Typography>

        {isLoading ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', p: 6 }}>
            <CircularProgress />
          </Box>
        ) : isError ? (
          <Alert severity="error">無法載入 dashboards。</Alert>
        ) : dashboards.length === 0 ? (
          <Alert severity="info">
            還沒有 dashboard。到 Data Analysis 查詢後按「加入 Dashboard」即可建立第一個。
          </Alert>
        ) : (
          <Box
            sx={{
              display: 'grid',
              gap: 2,
              gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', md: 'repeat(3, 1fr)' },
            }}
          >
            {dashboards.map((d) => (
              <Card key={d.id} variant="outlined">
                <CardActionArea component={Link} to={`/dashboards/${d.id}`} sx={{ p: 2 }}>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
                    <DashboardIcon fontSize="small" color="primary" />
                    <Typography variant="subtitle1" sx={{ fontWeight: 600 }} noWrap>
                      {d.name}
                    </Typography>
                  </Box>
                  <Typography variant="caption" color="text.secondary">
                    {d.widgetCount} 個 widget
                  </Typography>
                </CardActionArea>
              </Card>
            ))}
          </Box>
        )}
      </Box>
    </ThemeProvider>
  );
}
