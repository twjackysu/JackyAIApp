/**
 * Asking Studio AI from anywhere on the site: a page calls `askStudio(text)` (a button, a "brief me"
 * card) and Studio AI opens with that message sent, as if the reader had typed it. A window event, so a
 * page needs no handle on the assistant mounted once in `App.tsx`; where Studio AI is not shown (signed
 * out, a page with its own assistant) nothing happens.
 */
export const ASK_STUDIO_EVENT = 'studio:ask';

export interface AskStudioDetail {
  text: string;
  /** In a new conversation, not the one on screen. */
  fresh?: boolean;
}

export interface AskStudioOptions {
  /**
   * Start a new conversation for it rather than continuing the one on screen: a request that stands on
   * its own (the briefing) need not resend an old conversation's whole history, nor inherit that what the
   * old one read makes every write ask first (docs/ai-native DECISIONS D-055, D-061).
   */
  fresh?: boolean;
}

/** Opens Studio AI and sends `text`. */
export function askStudio(text: string, options?: AskStudioOptions): void {
  const message = text.trim();
  if (!message) return;
  const detail: AskStudioDetail = { text: message, ...(options?.fresh && { fresh: true }) };
  window.dispatchEvent(new CustomEvent<AskStudioDetail>(ASK_STUDIO_EVENT, { detail }));
}

/** What an `askStudio` event asks, or null for one that is not well formed. */
export function askedOf(event: Event): AskStudioDetail | null {
  const detail = (event as CustomEvent<unknown>).detail;
  if (typeof detail !== 'object' || detail === null) return null;
  const { text, fresh } = detail as { text?: unknown; fresh?: unknown };
  if (typeof text !== 'string' || !text.trim()) return null;
  return { text: text.trim().slice(0, 2000), ...(fresh === true && { fresh: true }) };
}

/** What the morning briefing asks (the home page's card); the server's prompt says what it covers.
 *  Always in a conversation of its own. */
export const BRIEFING_PROMPT = '給我今天的簡報';

/** Where the reader went wrong lately (`weak_spots`): the stats page's card, the stats and exam pages' first
 *  suggestion and the `/` command 找出我的弱點. */
export const FIND_MY_WEAK_SPOTS = '找出我最近英文最弱的地方';

/** What a dashboard's 「AI 解讀」 asks (its owner only): the server's prompt names the dashboard on screen, and
 *  `read_dashboard` reads it as it is now. Always in a conversation of its own. */
export const READ_DASHBOARD_PROMPT = '幫我解讀這個 dashboard：最重要的變化與值得注意的地方';
