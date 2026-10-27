/**
 * Where the focus goes once a conversation is deleted from the history column (D-187): the press's button goes with
 * its row, and a focus left on `<body>` sends a keyboard or screen-reader user back to the top of the page (or, in
 * Studio AI's drawer, to its first control). As a file manager does: the row that took its place (the next one), else
 * the one above it, else — the list now empty — the column's 新對話.
 */

/** The row to focus, by id, or `null` for 新對話. `shown` is the rows in the order on screen, the deleted one among them. */
export const focusAfterDelete = (shown: readonly string[], deletedId: string): string | null => {
  const at = shown.indexOf(deletedId);
  if (at < 0) return null;
  return shown[at + 1] ?? shown[at - 1] ?? null;
};
