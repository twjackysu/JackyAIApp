/** Canonical path segment for a word: trimmed + lowercased, then URL-encoded so multi-word
 * phrases stay a single segment. Matches the backend's own lookup, which lowercases the same
 * way (`DictionaryService.GetWordAsync`), so any casing a visitor types still resolves — this
 * only keeps the shareable/crawlable URL consistent. */
export const toWordSlug = (word: string) => encodeURIComponent(word.trim().toLowerCase());
