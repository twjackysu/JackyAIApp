import DashboardCustomizeIcon from '@mui/icons-material/DashboardCustomizeOutlined';
import { Box, Button, Typography } from '@mui/material';
import { useMemo, useState } from 'react';

import { AddToDashboardDialog } from '../viz/AddToDashboardDialog';
import { getCapability } from '../viz/capabilities';
import { computeDataShape } from '../viz/dataShape';
import { renderViz } from '../viz/renderViz';

import { parseChartCardData } from './chartCard';

import type { CustomCard } from '@/components/aiChat/cards/toolCard';

/**
 * A summarize result the assistant drew, with the page's own chart renderers; one that carries its
 * plan can be added to a dashboard, which re-runs it on every view.
 */
export default function ChartCardView({ card }: { card: CustomCard }) {
  const chart = useMemo(() => parseChartCardData(card.data), [card.data]);
  const [adding, setAdding] = useState(false);
  if (!chart) return null;
  const shape = computeDataShape(chart.headers, chart.rows, {
    groupHints: chart.groupHints,
    temporalHints: chart.temporalHints,
  });

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
        <Typography variant="subtitle2" sx={{ flex: 1 }}>
          {card.title ?? getCapability(chart.viz).label}
        </Typography>
        {chart.plan && (
          <Button
            size="small"
            startIcon={<DashboardCustomizeIcon fontSize="small" />}
            onClick={() => setAdding(true)}
          >
            加到 dashboard
          </Button>
        )}
      </Box>
      <Box sx={{ height: 280 }}>{renderViz(chart.viz, chart.rows, chart.headers, shape)}</Box>
      {chart.plan && (
        <AddToDashboardDialog
          open={adding}
          onClose={() => setAdding(false)}
          vizType={chart.viz}
          plan={chart.plan}
          gridDefault={getCapability(chart.viz).gridDefault}
        />
      )}
    </Box>
  );
}
