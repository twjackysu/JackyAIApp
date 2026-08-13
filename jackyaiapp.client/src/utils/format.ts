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

/** Compact magnitude formatting shared by every Portfolio chart's axis/tooltip labels — no
 * currency prefix (callers that want "NT$" prepend it themselves, since not every caller wants
 * one, e.g. bare axis ticks). Values >= 1M get an "X.XM" suffix; smaller values print in full.
 *
 * Single decimal place everywhere: this used to be duplicated across NetWorthChart (2 decimals),
 * BenchmarkChart, and ContributionWaterfallChart (1 decimal each) — same chart family, same axis
 * kind, different precision for no reason. Standardized on 1, the majority.
 */
export function fmtCompact(n: number): string {
  // Negating a zero (e.g. a fully-paid-off liability) produces JS's negative zero, which
  // toLocaleString renders as the string "-0" — normalize it away first.
  const v = n === 0 ? 0 : n;
  return Math.abs(v) >= 1_000_000 ? `${(v / 1_000_000).toFixed(1)}M` : fmtNum(v);
}

/** Same as fmtCompact, with an "NT$" prefix for values under the M-suffix threshold (compact
 * M-suffixed values already read as currency without a prefix, e.g. "8.7M"). */
export function fmtCompactTWD(n: number): string {
  const v = n === 0 ? 0 : n;
  return Math.abs(v) >= 1_000_000 ? `${(v / 1_000_000).toFixed(1)}M` : `NT$${fmtNum(v)}`;
}
