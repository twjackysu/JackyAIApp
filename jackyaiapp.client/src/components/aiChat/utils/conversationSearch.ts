/**
 * Finding a conversation in the history column by its title (D-143): the titles are short now that the AI names
 * conversations (D-141), so a few words find one. Only titles, on the client: no request and no AI.
 */

/** The search box shows once there are this many conversations; with fewer the list is read at a glance. */
export const SEARCH_FROM = 6;

/** Case and full-width forms ignored (「ＥＴＦ」 finds 「ETF」). */
const fold = (text: string) => text.normalize('NFKC').toLowerCase();

/**
 * The conversations whose title holds every word of `query` (split on spaces), in their order; all of them for
 * an empty query.
 */
export const matchConversations = <T extends { title: string }>(
  conversations: T[],
  query: string,
): T[] => {
  const words = fold(query).split(/\s+/).filter(Boolean);
  if (words.length === 0) return conversations;
  return conversations.filter((c) => {
    const title = fold(c.title);
    return words.every((w) => title.includes(w));
  });
};
