import Box from '@mui/material/Box';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Tooltip from '@mui/material/Tooltip';
import { useEffect, useMemo, useState } from 'react';

import { VIZ_CAPABILITIES, getCapability } from './capabilities';
import { computeDataShape } from './dataShape';
import { evaluate, pickDefault } from './matcher';
import { ChartViz } from './renderers/ChartViz';
import { TableViz } from './renderers/TableViz';
import { ResizableWidget } from './ResizableWidget';

import type { VizId } from './types';

interface VizSwitcherProps {
  rows: Record<string, unknown>[];
  headers: string[];
  /** Output column names of DateTrunc group-bys, for precise temporal detection. */
  temporalHints?: string[];
}

/**
 * Lets the user switch the current result between any visualization whose
 * constraints fit the data. Ineligible options are shown but disabled, with the
 * reason in their tooltip. The chosen viz renders inside a resizable widget that
 * starts at the viz's default size.
 */
export function VizSwitcher({ rows, headers, temporalHints }: VizSwitcherProps) {
  const hintKey = (temporalHints ?? []).join('|');
  const shape = useMemo(
    () => computeDataShape(headers, rows, temporalHints ?? []),
    // hintKey captures temporalHints content without depending on array identity
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [headers, rows, hintKey],
  );

  const eligibility = useMemo(
    () => new Map(VIZ_CAPABILITIES.map((c) => [c.id, evaluate(c, shape)])),
    [shape],
  );

  const [selected, setSelected] = useState<VizId>('table');

  // Re-pick a sensible default whenever the result shape changes.
  useEffect(() => {
    setSelected(pickDefault(shape, VIZ_CAPABILITIES));
  }, [shape]);

  const cap = getCapability(selected);

  return (
    <>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1.5 }}>
        <ToggleButtonGroup
          size="small"
          exclusive
          value={selected}
          onChange={(_e, v: VizId | null) => {
            if (v !== null) setSelected(v);
          }}
          aria-label="visualization type"
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

      <ResizableWidget key={selected} defaultSize={cap.defaultSize}>
        {selected === 'table' ? (
          <TableViz rows={rows} headers={headers} />
        ) : (
          <ChartViz chartType={selected} rows={rows} headers={headers} />
        )}
      </ResizableWidget>
    </>
  );
}
