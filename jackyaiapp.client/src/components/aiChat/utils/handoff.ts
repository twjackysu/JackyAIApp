/**
 * Handing a task from one assistant to another page's own (D-095): Studio AI asked for a dashboard goes to
 * /data-analysis, whose assistant has the data tools, and that assistant is asked there. The server sends the
 * question only with the navigation (`tool_result.handoff`, never once the conversation read outside text); the
 * page gets it in the router's location state, which only this site's own code can set (a link from elsewhere
 * cannot), and asks it once.
 */

/** The longest question handed over, as the server cuts it (`ClientToolRunner.MaxHandoffLength`). */
export const HANDOFF_MAX = 500;

/** Who hands tasks over: the name shown over the message the page's assistant is asked (D-099). */
export const HANDOFF_FROM = 'Studio AI';

const STATE_KEY = 'assistantHandoff';

/** The question a tool result hands over (`tool_result.handoff`), or undefined for none or anything else. */
export function handoffOf(raw: unknown): string | undefined {
  if (typeof raw !== 'string') return undefined;
  const text = raw.trim();
  if (!text) return undefined;
  return text.length > HANDOFF_MAX ? text.slice(0, HANDOFF_MAX) : text;
}

/** The location state that carries a question to the page's assistant (`navigate(path, { state })`). */
export const handoffState = (text: string): Record<string, string> => ({ [STATE_KEY]: text });

/** The question the page was handed in its location state, if any. */
export function handoffFromState(state: unknown): string | undefined {
  if (typeof state !== 'object' || state === null) return undefined;
  return handoffOf((state as Record<string, unknown>)[STATE_KEY]);
}
