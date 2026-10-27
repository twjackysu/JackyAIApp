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

/*
 * Finding a conversation by what was said in it (D-191): the server looks through the reader's stored messages once
 * they stop typing, and the conversations it finds join those found by their title, each with the words around the
 * find. Titles still answer at once, on the client.
 */

/** The longest search the box takes; the server refuses a longer one (`ChatConversationService.MaxSearchLength`). */
export const MAX_SEARCH_LENGTH = 100;

/** How long the typing has to pause before the messages are searched. */
export const SEARCH_PAUSE_MS = 350;

/** One conversation a search shows: found by its title, or by a message, whose snippet it then carries. */
export interface ConversationFind<T> {
  conversation: T;
  snippet?: string;
}

/**
 * The conversations `query` finds, in the list's order: those whose title holds every word, then (in the same order,
 * mixed in) those the server found in their messages, with the snippet. A find whose conversation the list does not
 * have (deleted meanwhile, or not yet read) is left out. All of them, without snippets, for an empty query.
 */
export const findConversations = <T extends { id: string; title: string }>(
  conversations: T[],
  query: string,
  messageHits: { conversationId: string; snippet: string }[] | undefined,
): ConversationFind<T>[] => {
  const byTitle = new Set(matchConversations(conversations, query).map((c) => c.id));
  const bySnippet = new Map((messageHits ?? []).map((h) => [h.conversationId, h.snippet]));
  return conversations
    .filter((c) => byTitle.has(c.id) || bySnippet.has(c.id))
    .map((c) =>
      byTitle.has(c.id) ? { conversation: c } : { conversation: c, snippet: bySnippet.get(c.id) },
    );
};

/**
 * A snippet in parts, those that are one of the query's words (any case) marked, to show what was found. Full-width
 * forms are only matched when folding them keeps the snippet's length (so the parts stay where they were).
 */
export const highlightParts = (text: string, query: string): { text: string; match: boolean }[] => {
  const folded = fold(text);
  const words = fold(query).split(/\s+/).filter(Boolean);
  if (words.length === 0 || folded.length !== text.length) return [{ text, match: false }];
  const marked = new Array<boolean>(text.length).fill(false);
  for (const word of words) {
    for (let at = folded.indexOf(word); at >= 0; at = folded.indexOf(word, at + word.length))
      marked.fill(true, at, at + word.length);
  }
  const parts: { text: string; match: boolean }[] = [];
  for (let i = 0; i < text.length; i++) {
    const last = parts[parts.length - 1];
    if (last && last.match === marked[i]) last.text += text[i];
    else parts.push({ text: text[i], match: marked[i] });
  }
  return parts;
};
