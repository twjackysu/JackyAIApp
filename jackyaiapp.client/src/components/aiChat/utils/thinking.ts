/**
 * The summary of the model's thinking that an answer streams before (and between) its text (the server's
 * `thinking` events, D-136). It is not the answer: it is shown folded above it, kept for this session only
 * (the server stores none of it, as with the tool steps), and dropped when the turn fails.
 */
export interface ThinkingTrace {
  /** The summary so far; a span of thinking that resumes after the answer or a tool starts a new paragraph. */
  text: string;
  /** Time spent thinking in spans already over, in milliseconds. */
  ms: number;
  /** When the span still going started (`Date.now()`); none once the answer or a tool took over. */
  since?: number;
}

/** A piece of thinking arrived: the first starts the trace, one after a pause starts a new span and paragraph. */
export function addThinking(
  trace: ThinkingTrace | undefined,
  piece: string,
  now: number,
): ThinkingTrace {
  if (!trace) return { text: piece, ms: 0, since: now };
  if (trace.since != null) return { ...trace, text: trace.text + piece };
  // The server may already start the new block with its paragraph break; one is enough.
  const before = trace.text.trimEnd();
  const after = piece.replace(/^\n+/, '');
  return { text: before ? `${before}\n\n${after}` : after, ms: trace.ms, since: now };
}

/** Something other than thinking arrived (answer text, a tool, the end): the span going on ends. The same
 *  object when none was, so a message that is not thinking is left as it is. */
export function pauseThinking(
  trace: ThinkingTrace | undefined,
  now: number,
): ThinkingTrace | undefined {
  if (trace?.since == null) return trace;
  return { text: trace.text, ms: trace.ms + Math.max(0, now - trace.since) };
}

/** Whether the model is thinking right now. */
export const isThinking = (trace: ThinkingTrace | undefined): boolean => trace?.since != null;

/** Whole seconds spent thinking, at least 1 (a summary came, so some thinking took place). */
export function thinkingSeconds(trace: ThinkingTrace, now: number): number {
  const live = trace.since != null ? Math.max(0, now - trace.since) : 0;
  return Math.max(1, Math.round((trace.ms + live) / 1000));
}

/** The folded trace's line: thinking now, or how long it took. */
export function thinkingLabel(trace: ThinkingTrace, now: number): string {
  return isThinking(trace)
    ? `思考中… ${thinkingSeconds(trace, now)} 秒`
    : `已思考 ${thinkingSeconds(trace, now)} 秒`;
}

const PREVIEW_LENGTH = 80;

/** The last line of the summary, for a folded trace to show while the model thinks; cut at a character, not
 *  inside a surrogate pair. */
export function latestThinkingLine(text: string): string {
  const lines = text.split('\n').map((line) => line.replace(/[*#_`>]+/g, '').trim());
  const last = [...lines].reverse().find((line) => line.length > 0) ?? '';
  const chars = Array.from(last);
  return chars.length > PREVIEW_LENGTH ? `${chars.slice(0, PREVIEW_LENGTH).join('')}…` : last;
}
