import Button from '@mui/material/Button';
import ButtonGroup from '@mui/material/ButtonGroup';

import { DayRange } from './dayRanges';

interface RangePickerProps {
  ranges: DayRange[];
  value: number;
  onChange: (days: number) => void;
}

/** Day-range button group shared by NetWorthChart/DrawdownChart/BenchmarkChart — each chart still
 * supplies its own `ranges` since the backend's clamp window differs per endpoint (net worth
 * history clamps to [7,180] days; drawdown/benchmark to [30,1095] — see dayRanges.ts). */
export function RangePicker({ ranges, value, onChange }: RangePickerProps) {
  return (
    <ButtonGroup size="small" variant="outlined">
      {ranges.map((r) => (
        <Button
          key={r.days}
          onClick={() => onChange(r.days)}
          variant={value === r.days ? 'contained' : 'outlined'}
        >
          {r.label}
        </Button>
      ))}
    </ButtonGroup>
  );
}
