/** Canonical path segment for a word: trimmed + lowercased, then URL-encoded so multi-word
 * phrases stay a single segment. Matches the backend's own lookup, which lowercases the same
 * way (`DictionaryService.GetWordAsync`), so any casing a visitor types still resolves — this
 * only keeps the shareable/crawlable URL consistent. */
export const toWordSlug = (word: string) => encodeURIComponent(word.trim().toLowerCase());

/** Canonical dictionary route for a word — the one place that formats this path, so every
 * internal link (search box, recently-searched, spell suggestions, cloze test recap, …)
 * agrees with the sitemap/canonical-link URL shape. */
export const toWordPath = (word: string) => `/english/dictionary/${toWordSlug(word)}`;
