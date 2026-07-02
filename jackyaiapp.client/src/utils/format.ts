/** Format a number with grouping and a fixed number of decimals (default 0). */
export const fmtNum = (n: number, decimals = 0) =>
  n.toLocaleString('zh-TW', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });

/**
 * Format a share/unit quantity: shows fractional digits only when present (up to 6),
 * so whole lots read as "1,000" while fractional shares/fund units keep their decimals.
 */
export const fmtQty = (n: number) => n.toLocaleString('zh-TW', { maximumFractionDigits: 6 });

/** Green for gains, red for losses, caller-supplied neutral colour for zero. */
export const pnlColor = (n: number, neutral = 'text.secondary') =>
  n > 0 ? '#22c55e' : n < 0 ? '#ef4444' : neutral;
