import { useMemo, useState } from 'react';

import {
  availablePresets,
  loadRange,
  rangeStartIndex,
  saveRange,
  type RangeChoice,
  type RangePreset,
  type RangePresetId,
} from '../../utils/trendlineRange';

/**
 * Which range a choice shows as picked: a preset no longer offered (the history got shorter than
 * it) reads as 全部; a start date of its own picks none.
 */
export function shownPresetOf(range: RangeChoice, presets: RangePreset[]): RangePresetId | null {
  if (!('preset' in range)) return null;
  return presets.some((p) => p.id === range.preset) ? range.preset : 'all';
}

/** Where the chart starts: the choice stored for this code, else `defaultRange`; a pick is kept. */
export function useRangeChoice(code: string, allDates: string[], defaultRange: RangePresetId) {
  const [range, setRange] = useState<RangeChoice>(
    () => loadRange(code) ?? { preset: defaultRange },
  );
  const presets = useMemo(() => availablePresets(allDates), [allDates]);
  const pick = (next: RangeChoice) => {
    setRange(next);
    saveRange(code, next);
  };
  return {
    presets,
    start: rangeStartIndex(allDates, range),
    shownPreset: shownPresetOf(range, presets),
    pick,
  };
}
