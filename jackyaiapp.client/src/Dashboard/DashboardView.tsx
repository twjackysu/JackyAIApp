import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import { ThemeProvider } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { useEffect, useRef, useState } from 'react';
import { GridLayout, noCompactor } from 'react-grid-layout';
import { Link, useParams } from 'react-router-dom';

import { DashboardWidgetView } from './DashboardWidgetView';

import { useGetDashboardQuery } from '@/apis/dashboardApis';
import { dataAnalysisTheme } from '@/DataAnalysis/theme';
import { GRID_COLS, GRID_ROW_HEIGHT } from '@/DataAnalysis/viz/ResizableWidget';

import 'react-grid-layout/css/styles.css';

export default function DashboardView() {
  const { id = '' } = useParams();
  const { data, isLoading, isError } = useGetDashboardQuery(id, { skip: !id });
  const dashboard = data?.data;

  const containerRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const el = containerRef.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const measure = () => setWidth(el.clientWidth);
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const widgets = dashboard?.widgets ?? [];
  const layout = widgets.map((w) => ({ i: w.id, x: w.x, y: w.y, w: w.w, h: w.h, static: true }));

  return (
    <ThemeProvider theme={dataAnalysisTheme}>
      <Box>
        <Button
          component={Link}
          to="/dashboards"
          size="small"
          startIcon={<ArrowBackIcon />}
          sx={{ mb: 1 }}
        >
          所有 Dashboards
        </Button>

        {isLoading ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', p: 6 }}>
            <CircularProgress />
          </Box>
        ) : isError || !dashboard ? (
          <Alert severity="error">找不到這個 dashboard，或你沒有存取權限。</Alert>
        ) : (
          <>
            <Typography variant="h5" sx={{ fontWeight: 700, mb: 2 }}>
              {dashboard.name}
            </Typography>

            {widgets.length === 0 ? (
              <Alert severity="info">
                這個 dashboard 還沒有 widget。到 Data Analysis 查詢後按「加入 Dashboard」即可。
              </Alert>
            ) : (
              <Box ref={containerRef}>
                {width > 0 && (
                  <GridLayout
                    width={width}
                    layout={layout}
                    gridConfig={{
                      cols: GRID_COLS,
                      rowHeight: GRID_ROW_HEIGHT,
                      margin: [12, 12],
                      containerPadding: [0, 0],
                    }}
                    dragConfig={{ enabled: false }}
                    resizeConfig={{ enabled: false }}
                    compactor={noCompactor}
                    autoSize
                  >
                    {widgets.map((w) => (
                      <div key={w.id}>
                        <DashboardWidgetView widget={w} />
                      </div>
                    ))}
                  </GridLayout>
                )}
              </Box>
            )}
          </>
        )}
      </Box>
    </ThemeProvider>
  );
}
