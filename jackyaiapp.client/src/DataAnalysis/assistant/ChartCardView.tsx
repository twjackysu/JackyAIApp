import DashboardCustomizeIcon from '@mui/icons-material/DashboardCustomizeOutlined';
import { Box, Button, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useMemo, useState } from 'react';

import { AddToDashboardDialog } from '../viz/AddToDashboardDialog';
import { getCapability, VIZ_CAPABILITIES } from '../viz/capabilities';
import { computeDataShape } from '../viz/dataShape';
import { evaluate } from '../viz/matcher';
import { renderViz } from '../viz/renderViz';

import { parseChartCardData } from './chartCard';

import type { VizId } from '../viz/types';
import type { CustomCard } from '@/components/aiChat/cards/toolCard';

import { ActionTooltip } from '@/components/hints';

/**
 * A summarize result the assistant drew, with the page's own chart renderers. The reader can draw it
 * as any other chart its data fits, right there (no question to the assistant, no charge), and one that
 * carries its plan can be added to a dashboard as the chart on screen, which re-runs it on every view.
 */
export default function ChartCardView({ card }: { card: CustomCard }) {
  const chart = useMemo(() => parseChartCardData(card.data), [card.data]);
  const [adding, setAdding] = useState(false);
  // The reader's pick; until then, the assistant's.
  const [picked, setPicked] = useState<VizId | null>(null);
  const shape = useMemo(
    () =>
      chart &&
      computeDataShape(chart.headers, chart.rows, {
        groupHints: chart.groupHints,
        temporalHints: chart.temporalHints,
      }),
    [chart],
  );
  // Only the charts the data fits: the card is small, and the page's own switcher explains the rest.
  const fitting = useMemo(
    () => (shape ? VIZ_CAPABILITIES.filter((c) => evaluate(c, shape).ok) : []),
    [shape],
  );
  if (!chart || !shape) return null;
  const viz = picked ?? chart.viz;

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1, flexWrap: 'wrap' }}>
        <Typography variant="subtitle2" sx={{ flex: 1, minWidth: 120 }}>
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
      {fitting.length > 1 && (
        <ToggleButtonGroup
          size="small"
          exclusive
          value={viz}
          onChange={(_e, v: VizId | null) => {
            if (v !== null) setPicked(v);
          }}
          aria-label="換一種圖表"
          sx={{ flexWrap: 'wrap', mb: 1, '& .MuiToggleButton-root': { p: 0.5 } }}
        >
          {fitting.map((c) => (
            <ActionTooltip key={c.id} title={c.label}>
              <ToggleButton value={c.id} aria-label={c.label}>
                {c.icon}
              </ToggleButton>
            </ActionTooltip>
          ))}
        </ToggleButtonGroup>
      )}
      <Box sx={{ height: 280 }}>{renderViz(viz, chart.rows, chart.headers, shape)}</Box>
      {chart.plan && (
        <AddToDashboardDialog
          open={adding}
          onClose={() => setAdding(false)}
          vizType={viz}
          plan={chart.plan}
          gridDefault={getCapability(viz).gridDefault}
        />
      )}
    </Box>
  );
}
