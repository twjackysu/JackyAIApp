/** Multi-stock peer comparison tab caps at 3 side-by-side columns — see TASK-014 / ARCHITECTURE.md. */
export const MAX_COMPARISON_CODES = 3;

/**
 * Adds a trimmed code to the comparison list, deduping case-insensitively and refusing
 * once the list is at {@link MAX_COMPARISON_CODES}. Returns the same array reference when
 * nothing changes, so callers can use it directly as a `setState` updater.
 */
export function addComparisonCode(codes: string[], rawCode: string): string[] {
  const code = rawCode.trim();
  if (!code) return codes;
  if (codes.length >= MAX_COMPARISON_CODES) return codes;
  if (codes.some((c) => c.toUpperCase() === code.toUpperCase())) return codes;
  return [...codes, code];
}

export function removeComparisonCode(codes: string[], code: string): string[] {
  return codes.filter((c) => c !== code);
}
