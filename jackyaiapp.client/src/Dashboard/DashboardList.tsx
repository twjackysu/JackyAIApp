import AssessmentIcon from '@mui/icons-material/Assessment';
import DashboardIcon from '@mui/icons-material/Dashboard';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import Chip from '@mui/material/Chip';
import { ThemeProvider } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { Link } from 'react-router-dom';

import { useGetDashboardsQuery } from '@/apis/dashboardApis';
import CenteredSpinner from '@/components/CenteredSpinner';
import { DataAnalysisNav } from '@/components/DataAnalysisNav';
import { dataAnalysisTheme } from '@/DataAnalysis/theme';

export default function DashboardList() {
  const { data, isLoading, isError } = useGetDashboardsQuery();
  const dashboards = data?.data ?? [];

  return (
    <ThemeProvider theme={dataAnalysisTheme}>
      <Box sx={{ maxWidth: 900, mx: 'auto' }}>
        <DataAnalysisNav />
        <Typography variant="h5" sx={{ fontWeight: 700, mb: 2 }}>
          我的儀表板
        </Typography>

        {isLoading ? (
          <CenteredSpinner sx={{ p: 6 }} />
        ) : isError ? (
          <Alert severity="error">無法載入 dashboards。</Alert>
        ) : dashboards.length === 0 ? (
          <Box
            sx={{
              textAlign: 'center',
              border: '1px dashed',
              borderColor: 'divider',
              borderRadius: 3,
              px: 3,
              py: 8,
              mt: 2,
            }}
          >
            <DashboardIcon sx={{ fontSize: 48, color: 'text.disabled', mb: 1.5 }} />
            <Typography variant="h6" sx={{ fontWeight: 600, mb: 0.5 }}>
              還沒有任何 Dashboard
            </Typography>
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ maxWidth: 420, mx: 'auto', mb: 3 }}
            >
              到 Data Analysis 用自然語言或手動查詢資料，得到結果後按「加入
              Dashboard」，就能組出你的第一個儀表板。
            </Typography>
            <Button
              component={Link}
              to="/data-analysis"
              variant="contained"
              startIcon={<AssessmentIcon />}
            >
              前往 Data Analysis
            </Button>
          </Box>
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
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flexWrap: 'wrap' }}>
                    <Typography variant="caption" color="text.secondary">
                      {d.widgetCount} 個 widget
                    </Typography>
                    {!d.isOwner && <Chip size="small" variant="outlined" label="共享給我" />}
                    {d.isOwner && d.visibility !== 'Private' && (
                      <Chip
                        size="small"
                        variant="outlined"
                        label={
                          d.visibility === 'Public'
                            ? '登入可看'
                            : d.visibility === 'Anyone'
                              ? '完全公開'
                              : '指定使用者'
                        }
                      />
                    )}
                  </Box>
                </CardActionArea>
              </Card>
            ))}
          </Box>
        )}
      </Box>
    </ThemeProvider>
  );
}
