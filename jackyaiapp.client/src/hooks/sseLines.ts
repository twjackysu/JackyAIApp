/**
 * Splits a server-sent event stream into the payloads of its `data:` lines, across the chunks the
 * network hands it in. A chunk can end anywhere — mid-line, mid-event — so the unfinished last line
 * is carried over in `rest` and completed by the next chunk; reading each chunk on its own dropped
 * any event split between two (a long tool result always is). Lines that are not `data:` (a
 * `: ping` comment kept the connection alive, an empty separator) carry nothing and are skipped.
 */
export interface SseLines {
  /** The payload after `data: ` of each completed line, in order. */
  data: string[];
  /** The unfinished last line, to pass back with the next chunk. */
  rest: string;
}

const DATA_PREFIX = 'data: ';

export const splitSseLines = (rest: string, chunk: string): SseLines => {
  const lines = (rest + chunk).split('\n');
  const unfinished = lines.pop() ?? '';
  return { data: lines.flatMap(dataOf), rest: unfinished };
};

/** What is left once the stream has ended: a last line the server did not end with a newline. */
export const finishSseLines = (rest: string): string[] => dataOf(rest);

const dataOf = (line: string): string[] => {
  const text = line.endsWith('\r') ? line.slice(0, -1) : line;
  return text.startsWith(DATA_PREFIX) && text.trim() !== DATA_PREFIX.trim()
    ? [text.slice(DATA_PREFIX.length)]
    : [];
};
