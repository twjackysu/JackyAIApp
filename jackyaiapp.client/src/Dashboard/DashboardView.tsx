import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import CloseIcon from '@mui/icons-material/Close';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import { ThemeProvider } from '@mui/material/styles';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useRef, useState } from 'react';
import { GridLayout, verticalCompactor } from 'react-grid-layout';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { DashboardWidgetView } from './DashboardWidgetView';

import {
  useDeleteDashboardMutation,
  useGetDashboardQuery,
  useRemoveWidgetMutation,
  useRenameDashboardMutation,
  useUpdateLayoutMutation,
} from '@/apis/dashboardApis';
import { dataAnalysisTheme } from '@/DataAnalysis/theme';
import { GRID_COLS, GRID_ROW_HEIGHT } from '@/DataAnalysis/viz/ResizableWidget';

import 'react-grid-layout/css/styles.css';

interface LayoutItem {
  i: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

export default function DashboardView() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const { data, isLoading, isError } = useGetDashboardQuery(id, { skip: !id });
  const dashboard = data?.data;
  const widgets = dashboard?.widgets ?? [];

  const [renameDashboard] = useRenameDashboardMutation();
  const [deleteDashboard, { isLoading: deleting }] = useDeleteDashboardMutation();
  const [updateLayout, { isLoading: savingLayout }] = useUpdateLayoutMutation();
  const [removeWidget] = useRemoveWidgetMutation();

  const [editing, setEditing] = useState(false);
  const [nameInput, setNameInput] = useState('');
  const [localLayout, setLocalLayout] = useState<LayoutItem[]>([]);
  const [confirmDelete, setConfirmDelete] = useState(false);

  // width for react-grid-layout's base GridLayout
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

  const enterEdit = () => {
    if (!dashboard) return;
    setNameInput(dashboard.name);
    setLocalLayout(widgets.map((w) => ({ i: w.id, x: w.x, y: w.y, w: w.w, h: w.h })));
    setEditing(true);
  };

  const handleDone = async () => {
    try {
      await updateLayout({
        id,
        items: localLayout.map(({ i, x, y, w, h }) => ({ id: i, x, y, w, h })),
      }).unwrap();
    } catch {
      /* keep editing on failure */
      return;
    }
    setEditing(false);
  };

  const handleRename = () => {
    const n = nameInput.trim();
    if (n && dashboard && n !== dashboard.name) renameDashboard({ id, name: n });
  };

  const handleRemoveWidget = async (widgetId: string) => {
    setLocalLayout((prev) => prev.filter((it) => it.i !== widgetId));
    await removeWidget({ dashboardId: id, widgetId });
  };

  const handleDeleteDashboard = async () => {
    try {
      await deleteDashboard(id).unwrap();
      navigate('/dashboards');
    } catch {
      setConfirmDelete(false);
    }
  };

  const layout: (LayoutItem & { static?: boolean })[] = editing
    ? localLayout
    : widgets.map((w) => ({ i: w.id, x: w.x, y: w.y, w: w.w, h: w.h, static: true }));

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
            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 2,
                mb: 2,
                flexWrap: 'wrap',
              }}
            >
              {editing ? (
                <TextField
                  size="small"
                  value={nameInput}
                  onChange={(e) => setNameInput(e.target.value)}
                  onBlur={handleRename}
                  onKeyDown={(e) => e.key === 'Enter' && handleRename()}
                  sx={{ flexGrow: 1, maxWidth: 360 }}
                />
              ) : (
                <Typography variant="h5" sx={{ fontWeight: 700 }}>
                  {dashboard.name}
                </Typography>
              )}

              <Box sx={{ display: 'flex', gap: 1 }}>
                {editing ? (
                  <>
                    <Button
                      color="error"
                      size="small"
                      startIcon={<DeleteIcon />}
                      onClick={() => setConfirmDelete(true)}
                    >
                      刪除
                    </Button>
                    <Button
                      variant="contained"
                      size="small"
                      onClick={handleDone}
                      disabled={savingLayout}
                    >
                      {savingLayout ? '儲存中…' : '完成'}
                    </Button>
                  </>
                ) : (
                  widgets.length > 0 && (
                    <Button size="small" startIcon={<EditIcon />} onClick={enterEdit}>
                      編輯版面
                    </Button>
                  )
                )}
              </Box>
            </Box>

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
                    dragConfig={{ enabled: editing }}
                    resizeConfig={{ enabled: editing }}
                    compactor={verticalCompactor}
                    onLayoutChange={(l) => editing && setLocalLayout(l as LayoutItem[])}
                    autoSize
                  >
                    {widgets.map((w) => (
                      <div key={w.id}>
                        <Box sx={{ position: 'relative', height: '100%' }}>
                          {editing && (
                            <IconButton
                              size="small"
                              onClick={() => handleRemoveWidget(w.id)}
                              sx={{
                                position: 'absolute',
                                top: 2,
                                right: 2,
                                zIndex: 2,
                                bgcolor: 'background.paper',
                                '&:hover': { bgcolor: 'action.hover' },
                              }}
                            >
                              <CloseIcon fontSize="small" />
                            </IconButton>
                          )}
                          {/* During edit, block content interaction so the chart canvas
                              doesn't swallow drag/resize gestures. */}
                          <Box sx={{ height: '100%', pointerEvents: editing ? 'none' : 'auto' }}>
                            <DashboardWidgetView widget={w} />
                          </Box>
                        </Box>
                      </div>
                    ))}
                  </GridLayout>
                )}
              </Box>
            )}
          </>
        )}

        <Dialog open={confirmDelete} onClose={() => setConfirmDelete(false)}>
          <DialogTitle>刪除 Dashboard？</DialogTitle>
          <DialogContent>
            <Typography variant="body2">
              「{dashboard?.name}」及其所有 widget 將被刪除，無法復原。
            </Typography>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setConfirmDelete(false)} disabled={deleting}>
              取消
            </Button>
            <Button
              color="error"
              variant="contained"
              onClick={handleDeleteDashboard}
              disabled={deleting}
            >
              {deleting ? '刪除中…' : '刪除'}
            </Button>
          </DialogActions>
        </Dialog>
      </Box>
    </ThemeProvider>
  );
}
