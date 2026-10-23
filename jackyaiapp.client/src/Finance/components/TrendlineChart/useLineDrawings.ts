import { useState } from 'react';

import {
  clearDrawings,
  lineGeometry,
  loadDrawings,
  saveDrawings,
  type DrawnLine,
} from '../../utils/trendlineDrawings';

import type { PriceBarsData } from '@/apis/financeApis/types';

/** Undo steps kept. */
export const UNDO_DEPTH = 20;

/**
 * The lines on a chart: the automatic ones until the user changes one, then the user's drawing,
 * kept per code in this browser. Every change can be undone.
 */
export function useLineDrawings(
  code: string,
  allBars: PriceBarsData['bars'],
  autoLines: DrawnLine[],
) {
  const [custom, setCustom] = useState<DrawnLine[] | null>(() => loadDrawings(code));
  const [past, setPast] = useState<(DrawnLine[] | null)[]>([]);
  const lines = custom ?? autoLines;

  const store = (next: DrawnLine[] | null) => {
    setCustom(next);
    if (next === null) clearDrawings(code);
    else saveDrawings(code, next);
  };
  /** Every change goes through here, so it can be undone. `null` is back to the automatic lines. */
  const apply = (next: DrawnLine[] | null) => {
    setPast((p) => [...p.slice(1 - UNDO_DEPTH), custom]);
    // A line wholly before the first bar of the history can never show again: let it go. One
    // that only falls before the chosen start date stays, for when the chart starts earlier.
    store(next && next.filter((l) => lineGeometry(allBars, { atrs: [], swings: [] }, l) !== null));
  };
  const undo = () => {
    store(past[past.length - 1]);
    setPast((p) => p.slice(0, -1));
  };

  return {
    lines,
    /** Whether the lines are the user's own (not the automatic ones). */
    customized: custom !== null,
    canUndo: past.length > 0,
    undo,
    change: (line: DrawnLine) => apply(lines.map((l) => (l.id === line.id ? line : l))),
    remove: (id: string) => apply(lines.filter((l) => l.id !== id)),
    add: (line: DrawnLine) => apply([...lines, line]),
    clear: () => apply([]),
    restoreAutomatic: () => apply(null),
  };
}
