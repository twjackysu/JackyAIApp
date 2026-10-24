/**
 * Asking Studio AI from anywhere on the site: a page calls `askStudio(text)` (a button, a "brief me"
 * card) and Studio AI opens with that message sent, as if the reader had typed it. A window event, so a
 * page needs no handle on the assistant mounted once in `App.tsx`; where Studio AI is not shown (signed
 * out, a page with its own assistant) nothing happens.
 */
export const ASK_STUDIO_EVENT = 'studio:ask';

export interface AskStudioDetail {
  text: string;
}

/** Opens Studio AI and sends `text`. */
export function askStudio(text: string): void {
  const message = text.trim();
  if (!message) return;
  window.dispatchEvent(
    new CustomEvent<AskStudioDetail>(ASK_STUDIO_EVENT, { detail: { text: message } }),
  );
}

/** The message an `askStudio` event carries, or null for one that is not well formed. */
export function askedText(event: Event): string | null {
  const detail = (event as CustomEvent<unknown>).detail;
  if (typeof detail !== 'object' || detail === null) return null;
  const text = (detail as { text?: unknown }).text;
  return typeof text === 'string' && text.trim() ? text.trim().slice(0, 2000) : null;
}

/** What the morning briefing asks (the home page's card); the server's prompt says what it covers. */
export const BRIEFING_PROMPT = '給我今天的簡報';
