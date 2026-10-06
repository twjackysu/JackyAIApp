/** Letters (incl. accented), with single spaces/hyphens/apostrophes between — i.e. a word or
 * simple phrase. Anything else (digits, punctuation, empty) stays plain text, not a link. */
const LINKABLE_WORD = /^[\p{L}]+(?:[ '-][\p{L}]+)*$/u;

export function isLinkableWord(text: string): boolean {
  return LINKABLE_WORD.test(text.trim());
}
