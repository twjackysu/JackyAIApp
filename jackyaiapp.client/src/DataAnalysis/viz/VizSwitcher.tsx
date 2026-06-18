import Box from '@mui/material/Box';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Tooltip from '@mui/material/Tooltip';
import { useMemo, useState } from 'react';

import { VIZ_CAPABILITIES, getCapability } from './capabilities';
import { computeDataShape } from './dataShape';
import { evaluate, pickDefault } from './matcher';
import { ChartViz } from './renderers/ChartViz';
import { HierarchyViz } from './renderers/HierarchyViz';
import { KpiViz } from './renderers/KpiViz';
import { SankeyViz } from './renderers/SankeyViz';
import { TableViz } from './renderers/TableViz';
import { WaterfallViz } from './renderers/WaterfallViz';
import { ResizableWidget } from './ResizableWidget';

import type { DataShape, VizId } from './types';

interface VizSwitcherProps {
  rows: Record<string, unknown>[];
  headers: string[];
  /** Output column names of ALL group-bys (so numeric dimensions aren't mistaken for measures). */
  groupHints?: string[];
  /** Output column names of DateTrunc group-bys, for precise temporal detection. */
  temporalHints?: string[];
}

/**
 * Lets the user switch the current result between any visualization whose
 * constraints fit the data. Ineligible options are shown but disabled, with the
 * reason in their tooltip. The chosen viz renders inside a resizable widget that
 * starts at the viz's default size.
 */
export function VizSwitcher({ rows, headers, groupHints, temporalHints }: VizSwitcherProps) {
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

  const [selected, setSelected] = useState<VizId>(() => pickDefault(shape, VIZ_CAPABILITIES));

  // Re-pick a sensible default whenever the result shape changes — adjust state
  // during render (shape is a stable memo, so this only fires on a new result)
  // instead of a sync useEffect.
  const [prevShape, setPrevShape] = useState(shape);
  if (prevShape !== shape) {
    setPrevShape(shape);
    setSelected(pickDefault(shape, VIZ_CAPABILITIES));
  }

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
    </>
  );
}

function renderViz(
  id: VizId,
  rows: Record<string, unknown>[],
  headers: string[],
  shape: DataShape,
) {
  // Use the authoritative classification from the shape (hint-aware) so numeric
  // dimensions (e.g. DateTrunc/Year) aren't treated as measures.
  const { groupKeys, measureKeys, temporalKey } = shape;
  switch (id) {
    case 'table':
      return <TableViz rows={rows} headers={headers} />;
    case 'kpi':
      return <KpiViz rows={rows} measureKeys={measureKeys} temporalKey={temporalKey} />;
    case 'treemap':
    case 'sunburst':
    case 'tree':
      return <HierarchyViz mode={id} rows={rows} groupKeys={groupKeys} measureKeys={measureKeys} />;
    case 'waterfall':
      return <WaterfallViz rows={rows} measureKeys={measureKeys} temporalKey={temporalKey} />;
    case 'sankey':
      return <SankeyViz rows={rows} groupKeys={groupKeys} measureKeys={measureKeys} />;
    default:
      // column / bar / line / pie / donut / stackedColumn / stackedBar
      return (
        <ChartViz chartType={id} rows={rows} groupKeys={groupKeys} measureKeys={measureKeys} />
      );
  }
}
