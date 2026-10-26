/** At most this much of either text is compared (the draft's end), so a long draft costs no more than this. */
const MAX_COMPARED = 1000;

/**
 * Whether a draft still holds the words the microphone heard (D-120), so its answer is read aloud: at least half
 * of what was heard is still in the draft, in order (white space aside). Fixing a misheard word keeps it spoken;
 * deleting what was heard and typing something else instead does not.
 */
export function stillSpoken(draft: string, heard: string): boolean {
  const h = [...heard.replace(/\s+/g, '')].slice(0, MAX_COMPARED);
  if (h.length === 0) return false;
  // The end of the draft: what was heard goes after what was typed (a long paste first keeps it in reach).
  const d = [...draft.replace(/\s+/g, '')].slice(-MAX_COMPARED);
  return commonInOrder(d, h) * 2 >= h.length;
}

/** The length of the longest run of characters both have in the same order (not necessarily adjacent). */
function commonInOrder(a: readonly string[], b: readonly string[]): number {
  let previous = new Array<number>(b.length + 1).fill(0);
  for (const x of a) {
    const row = new Array<number>(b.length + 1).fill(0);
    for (let j = 1; j <= b.length; j++)
      row[j] = x === b[j - 1] ? previous[j - 1] + 1 : Math.max(previous[j], row[j - 1]);
    previous = row;
  }
  return previous[b.length];
}
