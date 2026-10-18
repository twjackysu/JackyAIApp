export interface DayRange {
  label: string;
  days: number;
}

// The 1M/3M/6M set for the net-worth history charts (net worth and its period-over-period change):
// GetNetWorthHistoryAsync clamps to [7, 180] days server-side, so nothing longer can be offered.
export const NET_WORTH_RANGES: DayRange[] = [
  { label: '1M', days: 30 },
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
];

// The 3M/6M/1Y/3Y set — shared by every chart whose backend query clamps to a wide window
// (drawdown/benchmark comparison both clamp to [30, 1095] days).
export const WIDE_RANGES: DayRange[] = [
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
  { label: '1Y', days: 365 },
  { label: '3Y', days: 1095 },
];
