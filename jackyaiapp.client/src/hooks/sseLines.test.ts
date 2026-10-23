import { describe, expect, it } from 'vitest';

import { finishSseLines, splitSseLines } from './sseLines';

/** Feeds the chunks through one after another, as the stream reader does. */
const readAll = (chunks: string[]) => {
  let rest = '';
  const data: string[] = [];
  for (const chunk of chunks) {
    const lines = splitSseLines(rest, chunk);
    data.push(...lines.data);
    rest = lines.rest;
  }
  return [...data, ...finishSseLines(rest)];
};

describe('splitSseLines', () => {
  it('reads every event of a chunk holding several', () => {
    expect(readAll(['data: {"a":1}\n\ndata: {"b":2}\n\n'])).toEqual(['{"a":1}', '{"b":2}']);
  });

  it('keeps an event split between two chunks', () => {
    expect(readAll(['data: {"type":"del', 'ta","text":"hi"}\n\n'])).toEqual([
      '{"type":"delta","text":"hi"}',
    ]);
  });

  it('keeps an event split over many chunks, even mid-prefix', () => {
    const event = `data: {"card":"${'x'.repeat(50)}"}\n\n`;
    expect(readAll(event.match(/.{1,7}/gs) ?? [])).toEqual([`{"card":"${'x'.repeat(50)}"}`]);
  });

  it('carries the unfinished line over in rest', () => {
    expect(splitSseLines('', 'data: {"a":1}\n\ndata: {"b"')).toEqual({
      data: ['{"a":1}'],
      rest: 'data: {"b"',
    });
  });

  it('skips keep-alive comments and blank lines', () => {
    expect(readAll([': ping\n\n', 'data: {"a":1}\n\n', ': ping\n\n'])).toEqual(['{"a":1}']);
  });

  it('reads lines ended with CRLF', () => {
    expect(readAll(['data: {"a":1}\r\n\r\n'])).toEqual(['{"a":1}']);
  });

  it('reads a last line the stream ended without a newline', () => {
    expect(readAll(['data: {"a":1}\n\ndata: {"b":2}'])).toEqual(['{"a":1}', '{"b":2}']);
  });

  it('skips an empty data line', () => {
    expect(readAll(['data: \n\n'])).toEqual([]);
  });
});
