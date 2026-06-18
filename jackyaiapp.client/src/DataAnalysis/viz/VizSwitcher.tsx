import AddIcon from '@mui/icons-material/Add';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Tooltip from '@mui/material/Tooltip';
import { useMemo, useState } from 'react';

import { AddToDashboardDialog } from './AddToDashboardDialog';
import { VIZ_CAPABILITIES, getCapability } from './capabilities';
import { computeDataShape } from './dataShape';
import { evaluate, pickDefault } from './matcher';
import { renderViz } from './renderViz';
import { ResizableWidget } from './ResizableWidget';

import type { VizId } from './types';
import type { SummarizeRequest } from '../../apis/dataAnalysisApis/types';

interface VizSwitcherProps {
  rows: Record<string, unknown>[];
  headers: string[];
  /** Output column names of ALL group-bys (so numeric dimensions aren't mistaken for measures). */
  groupHints?: string[];
  /** Output column names of DateTrunc group-bys, for precise temporal detection. */
  temporalHints?: string[];
  /** The re-runnable plan for the current result; enables "Add to dashboard" when provided. */
  currentPlan?: SummarizeRequest | null;
  canAddToDashboard?: boolean;
}

/**
 * Lets the user switch the current result between any visualization whose
 * constraints fit the data. Ineligible options are shown but disabled, with the
 * reason in their tooltip. The chosen viz renders inside a resizable widget that
 * starts at the viz's default size.
 */
export function VizSwitcher({
  rows,
  headers,
  groupHints,
  temporalHints,
  currentPlan,
  canAddToDashboard,
}: VizSwitcherProps) {
  const hintKey = `${(groupHints ?? []).join('|')}#${(temporalHints ?? []).join('|')}`;
  const shape = useMemo(
    () => computeDataShape(headers, rows, { groupHints, temporalHints }),
    // hintKey captures hint content without depending on array identity
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [headers, rows, hintKey],
  );

  const eligibility = useMemo(
    () => new Map(VIZ_CAPABILITIES.map((c) => [c.id, evaluate(c, shape)])),
    [shape],
  );

  // Default to the best-fit viz for this result. The parent remounts this
  // component (via a `key` per result), so the initializer re-runs on each new
  // result — no effect or in-render setState needed to "reset".
  const [selected, setSelected] = useState<VizId>(() => pickDefault(shape, VIZ_CAPABILITIES));
  const [dashboardOpen, setDashboardOpen] = useState(false);

  const cap = getCapability(selected);
  const showAdd = !!canAddToDashboard && !!currentPlan;

  return (
    <>
      <Box
        sx={{
          display: 'flex',
          justifyContent: showAdd ? 'space-between' : 'flex-end',
          alignItems: 'center',
          gap: 1,
          mb: 1.5,
          flexWrap: 'wrap',
        }}
      >
        {showAdd && (
          <Button
            size="small"
            variant="outlined"
            startIcon={<AddIcon />}
            onClick={() => setDashboardOpen(true)}
          >
            加入 Dashboard
          </Button>
        )}
        <ToggleButtonGroup
          size="small"
          exclusive
          value={selected}
          onChange={(_e, v: VizId | null) => {
            if (v !== null) setSelected(v);
          }}
          aria-label="visualization type"
          sx={{ flexWrap: 'wrap' }}
        >
          {VIZ_CAPABILITIES.map((c) => {
            const e = eligibility.get(c.id);
            const ok = e?.ok ?? false;
            return (
              <Tooltip key={c.id} title={ok ? c.label : `${c.label} — ${e?.reason ?? ''}`}>
                {/* span keeps the tooltip working while the button is disabled */}
                <span style={{ display: 'inline-flex' }}>
                  <ToggleButton value={c.id} disabled={!ok} aria-label={c.label}>
                    {c.icon}
                  </ToggleButton>
                </span>
              </Tooltip>
            );
          })}
        </ToggleButtonGroup>
      </Box>

      <ResizableWidget key={selected} gridDefault={cap.gridDefault}>
        {renderViz(selected, rows, headers, shape)}
      </ResizableWidget>

      {showAdd && currentPlan && (
        <AddToDashboardDialog
          open={dashboardOpen}
          onClose={() => setDashboardOpen(false)}
          vizType={selected}
          plan={currentPlan}
          gridDefault={cap.gridDefault}
        />
      )}
    </>
  );
}
