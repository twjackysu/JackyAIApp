import { useEffect } from 'react';

/** What the tab's title starts with while an assistant waits on the reader. */
export const WAITING_TITLE_MARK = '（等你確認）';

// How many chats wait right now: Studio AI's drawer stays mounted on a page with an assistant of its own.
let waiting = 0;
// While any waits: the app sets the title per page (react-helmet-async in App.tsx), and Studio AI's drawer stays
// open across pages, so a new page's title gets the mark again.
let observer: MutationObserver | null = null;

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
 * long (D-107). A title the page sets meanwhile is marked too; only the mark is taken off again.
 */
export function useWaitingTitle(active: boolean) {
  useEffect(() => {
    if (!active) return;
    waiting += 1;
    mark();
    if (!observer) {
      // Setting the title from here changes it too; `mark` leaves a marked title alone, so this settles at once.
      observer = new MutationObserver(mark);
      observer.observe(document.head, { subtree: true, childList: true, characterData: true });
    }
    return () => {
      waiting -= 1;
      if (waiting > 0) return;
      observer?.disconnect();
      observer = null;
      unmark();
    };
  }, [active]);
}
