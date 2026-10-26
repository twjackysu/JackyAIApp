/**
 * Renaming a conversation by hand (D-144): the AI names a new one (D-141), and the reader can still give it a title
 * of their own from the history column. The server keeps it as one line and refuses an empty or longer one, so the
 * box holds the same.
 */

/** The longest title the box takes: the server's `ChatConversationService.MaxTitleLength` (a test holds them together). */
export const MAX_TITLE_LENGTH = 40;

/**
 * The title to save from what was typed: one line, its spaces run together, as the server keeps it. Null when there
 * is nothing to save: nothing left, longer than the server takes, or the title it already has.
 */
export const renamedTitle = (draft: string, current: string): string | null => {
  // eslint-disable-next-line no-control-regex
  const title = draft.replace(/[\s\u0000-\u001f\u007f-\u009f]+/g, ' ').trim();
  if (!title || title === current) return null;
  // By code points, so an emoji is one as on the server (which counts a flag or a family as one too; never more).
  return [...title].length > MAX_TITLE_LENGTH ? null : title;
};
