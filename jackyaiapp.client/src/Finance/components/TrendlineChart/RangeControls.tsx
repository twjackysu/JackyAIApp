import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';

import {
  latestStart,
  type RangeChoice,
  type RangePreset,
  type RangePresetId,
} from '../../utils/trendlineRange';

interface RangeControlsProps {
  presets: RangePreset[];
  shownPreset: RangePresetId | null;
  /** The first day shown. */
  startDate: string;
  /** Every day of the history. */
  allDates: string[];
  onPick: (choice: RangeChoice) => void;
}

/** Where the chart starts: a preset range, or a start date of one's own. */
export function RangeControls({
  presets,
  shownPreset,
  startDate,
  allDates,
  onPick,
}: RangeControlsProps) {
  return (
    <Stack
      direction="row"
      spacing={1}
      sx={{ flexWrap: 'wrap', rowGap: 1, mb: 1.5, alignItems: 'center' }}
    >
      <ToggleButtonGroup
        size="small"
        exclusive
        value={shownPreset}
        onChange={(_, id: RangePresetId | null) => id && onPick({ preset: id })}
        aria-label="圖表區間"
        sx={{ flexWrap: 'wrap' }}
      >
        {presets.map((p) => (
          <ToggleButton
            key={p.id}
            value={p.id}
            sx={{ px: 0.75, py: 0.25, minWidth: 0, fontSize: '0.8125rem' }}
          >
            {p.label}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
      <TextField
        type="date"
        size="small"
        label="起始日期"
        value={startDate}
        onChange={(e) => {
          if (/^\d{4}-\d{2}-\d{2}$/.test(e.target.value)) onPick({ from: e.target.value });
        }}
        slotProps={{
          inputLabel: { shrink: true },
          htmlInput: { min: allDates[0], max: latestStart(allDates) },
        }}
        sx={{ width: 160 }}
      />
    </Stack>
  );
}
