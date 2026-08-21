import AddIcon from '@mui/icons-material/Add';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import { useMemo, useState } from 'react';

import { AddToDashboardDialog } from './AddToDashboardDialog';
import { VIZ_CAPABILITIES, getCapability } from './capabilities';
import { computeDataShape } from './dataShape';
import { evaluate, pickDefault } from './matcher';
import { renderViz } from './renderViz';
import { ResizableWidget } from './ResizableWidget';

import type { VizId } from './types';
import type { SummarizeRequest } from '../../apis/dataAnalysisApis/types';

import { ActionTooltip, InfoPopover } from '@/components/hints';

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

  const unavailable = VIZ_CAPABILITIES.filter((c) => !(eligibility.get(c.id)?.ok ?? false)).map(
    (c) => ({ id: c.id, label: c.label, reason: eligibility.get(c.id)?.reason ?? '' }),
  );

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
            const ok = eligibility.get(c.id)?.ok ?? false;
            const button = (
              <span style={{ display: 'inline-flex' }}>
                <ToggleButton value={c.id} disabled={!ok} aria-label={c.label}>
                  {c.icon}
                </ToggleButton>
              </span>
            );
            // A disabled button receives neither hover nor tap, so its reason can't live on
            // the button itself — it goes in the "why is this greyed out" popover instead.
            return ok ? (
              <ActionTooltip key={c.id} title={c.label}>
                {button}
              </ActionTooltip>
            ) : (
              <Box key={c.id} component="span" sx={{ display: 'inline-flex' }}>
                {button}
              </Box>
            );
          })}
        </ToggleButtonGroup>
        {unavailable.length > 0 && (
          <InfoPopover
            content={
              <>
                <div>以下圖表不適用目前的資料形狀：</div>
                <ul style={{ margin: '4px 0 0', paddingInlineStart: 20 }}>
                  {unavailable.map((u) => (
                    <li key={u.id}>{`${u.label} — ${u.reason}`}</li>
                  ))}
                </ul>
              </>
            }
            ariaLabel="為什麼有些圖表無法選擇"
          />
        )}
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
