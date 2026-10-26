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
  /** Its answer read aloud once done. */
  readAloud?: boolean;
}

export interface AskStudioOptions {
  /**
   * Start a new conversation for it rather than continuing the one on screen: a request that stands on
   * its own (the briefing) need not resend an old conversation's whole history, nor inherit that what the
   * old one read makes every write ask first (docs/ai-native DECISIONS D-055, D-061).
   */
  fresh?: boolean;
  /**
   * Read the answer aloud once it is done, with the browser's own voice (the briefing to listen to, D-120): nothing
   * more is sent to the server and nothing more is charged.
   */
  readAloud?: boolean;
}

/** Opens Studio AI and sends `text`. */
export function askStudio(text: string, options?: AskStudioOptions): void {
  const message = text.trim();
  if (!message) return;
  const detail: AskStudioDetail = {
    text: message,
    ...(options?.fresh && { fresh: true }),
    ...(options?.readAloud && { readAloud: true }),
  };
  window.dispatchEvent(new CustomEvent<AskStudioDetail>(ASK_STUDIO_EVENT, { detail }));
}

/** What an `askStudio` event asks, or null for one that is not well formed. */
export function askedOf(event: Event): AskStudioDetail | null {
  const detail = (event as CustomEvent<unknown>).detail;
  if (typeof detail !== 'object' || detail === null) return null;
  const { text, fresh, readAloud } = detail as {
    text?: unknown;
    fresh?: unknown;
    readAloud?: unknown;
  };
  if (typeof text !== 'string' || !text.trim()) return null;
  return {
    text: text.trim().slice(0, 2000),
    ...(fresh === true && { fresh: true }),
    ...(readAloud === true && { readAloud: true }),
  };
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

/** The longest connector name a try-it request quotes. */
const MAX_TRIED_NAME = 60;

/**
 * Trying a connector the reader just added (D-126: the dialog's last step, a connector's detail drawer): Studio AI
 * is asked, in a conversation of its own (whose tools are read anew, the new connector's among them), to show what
 * it can do with it by using one tool that only reads. The name may be what the server or a registry publisher
 * called itself, so it goes as one plain line within quotes, never as more of the request.
 */
export function tryConnectorPrompt(name: string): string {
  const plain = name
    .replace(/[\p{C}\s「」]+/gu, ' ')
    .trim()
    .slice(0, MAX_TRIED_NAME)
    .trim();
  const named = plain ? `「${plain}」` : '這個 connector';
  return `示範一下你能用${named}做什麼：挑一個只會讀取的工具實際用一次，再用兩三句話告訴我還能請你用它做哪些事。`;
}
