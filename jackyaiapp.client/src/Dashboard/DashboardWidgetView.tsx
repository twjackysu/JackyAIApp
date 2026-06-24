import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import { useMemo } from 'react';

import type { DashboardWidget } from '@/apis/dashboardApis/types';
import type { VizId } from '@/DataAnalysis/viz/types';

import { useGetWidgetDataQuery } from '@/apis/dashboardApis';
import { computeDataShape, deriveViewByHints } from '@/DataAnalysis/viz/dataShape';
import { renderViz } from '@/DataAnalysis/viz/renderViz';

interface DashboardWidgetViewProps {
  dashboardId: string;
  widget: DashboardWidget;
}

/** Renders one saved dashboard widget by re-running its plan and drawing its viz. */
export function DashboardWidgetView({ dashboardId, widget }: DashboardWidgetViewProps) {
  const { data, isLoading, isError } = useGetWidgetDataQuery({
    dashboardId,
    widgetId: widget.id,
  });
  const rows = useMemo(() => data?.data?.rows ?? [], [data]);
  const headers = rows.length > 0 ? Object.keys(rows[0]) : [];

  const { groupHints, temporalHints } = useMemo(
    () => deriveViewByHints(widget.plan.viewBy),
    [widget.plan.viewBy],
  );
  const shape = useMemo(
    () => computeDataShape(headers, rows, { groupHints, temporalHints }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [rows, groupHints, temporalHints],
  );

  return (
    <Box
      sx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
        overflow: 'hidden',
        bgcolor: 'background.paper',
      }}
    >
      <Typography
        variant="caption"
        sx={{
          px: 1.5,
          py: 0.75,
          fontWeight: 600,
          color: 'text.secondary',
          borderBottom: '1px solid',
          borderColor: 'divider',
          whiteSpace: 'nowrap',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        {widget.title || widget.vizType}
      </Typography>
      <Box sx={{ flexGrow: 1, minHeight: 0, p: 1 }}>
        {isLoading ? (
          <Skeleton variant="rounded" sx={{ height: '100%', minHeight: 120 }} />
        ) : isError ? (
          <Alert severity="error" sx={{ m: 1 }}>
            無法載入此 widget 的資料。
          </Alert>
        ) : headers.length === 0 ? (
          <Alert severity="info" sx={{ m: 1 }}>
            這個查詢目前沒有資料。
          </Alert>
        ) : (
          renderViz(widget.vizType as VizId, rows, headers, shape)
        )}
      </Box>
    </Box>
  );
}
