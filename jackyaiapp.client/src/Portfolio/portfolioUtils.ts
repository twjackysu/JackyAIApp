/** Format a number with zh-TW grouping and a fixed number of decimals (default 0). */
export const fmtNum = (n: number, decimals = 0) =>
  n.toLocaleString('zh-TW', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });

/** Green for gains, red for losses, caller-supplied neutral colour for zero. */
export const pnlColor = (n: number, neutral = 'text.secondary') =>
  n > 0 ? '#22c55e' : n < 0 ? '#ef4444' : neutral;
