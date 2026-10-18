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

/** How far back the server keeps stock prices (about six months, see PersistentTwHistoricalProvider). */
export const PRICE_HISTORY_DAYS = 183;

/**
 * Where the long-range charts (drawdown, benchmark comparison) start. Three months is well inside
 * the price history, so the first view is complete; a longer range is the user's choice.
 */
export const DEFAULT_WIDE_RANGE_DAYS = 90;

// The 3M/6M/1Y/3Y set — shared by every chart whose backend query clamps to a wide window
// (drawdown/benchmark comparison both clamp to [30, 1095] days).
export const WIDE_RANGES: DayRange[] = [
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
  { label: '1Y', days: 365 },
  { label: '3Y', days: 1095 },
];
