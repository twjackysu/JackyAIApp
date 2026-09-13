/** Canonical word path is always lowercase (matches the backend's own
 * `word.Trim().ToLower()` lookup key) and URL-encoded so multi-word phrases stay a
 * single path segment. */
export function wordPath(word: string): string {
  return `/english/dictionary/${encodeURIComponent(word.trim().toLowerCase())}`;
}
