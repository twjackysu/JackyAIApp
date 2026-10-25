import { useEffect } from 'react';

/** What the tab's title starts with while an assistant waits on the reader. */
export const WAITING_TITLE_MARK = '（等你確認）';

// How many chats wait right now: Studio AI's drawer stays mounted on a page with an assistant of its own.
let waiting = 0;

const mark = () => {
  if (!document.title.startsWith(WAITING_TITLE_MARK))
    document.title = WAITING_TITLE_MARK + document.title;
};

const unmark = () => {
  if (document.title.startsWith(WAITING_TITLE_MARK))
    document.title = document.title.slice(WAITING_TITLE_MARK.length);
};

/**
 * While `active` (a confirmation card waits), the tab's title starts with 「（等你確認）」, so a reader who
 * went to another tab while the assistant worked sees that it now needs them — the turn waits only so
 * long (D-107). Only the mark is taken off again, so a title changed meanwhile stays.
 */
export function useWaitingTitle(active: boolean) {
  useEffect(() => {
    if (!active) return;
    waiting += 1;
    mark();
    return () => {
      waiting -= 1;
      if (waiting === 0) unmark();
    };
  }, [active]);
}
