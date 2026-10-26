import { describe, expect, it } from 'vitest';

import {
  addThinking,
  isThinking,
  latestThinkingLine,
  pauseThinking,
  thinkingLabel,
  thinkingSeconds,
} from './thinking';

describe('thinking trace', () => {
  it('starts with the first piece and joins the next ones of the same span', () => {
    const trace = addThinking(addThinking(undefined, '先看看', 1000), '今天的行情。', 1500);
    expect(trace).toEqual({ text: '先看看今天的行情。', ms: 0, since: 1000 });
    expect(isThinking(trace)).toBe(true);
  });

  it('a pause ends the span and counts its time', () => {
    const trace = pauseThinking(addThinking(undefined, '想', 1000), 4200);
    expect(trace).toEqual({ text: '想', ms: 3200 });
    expect(isThinking(trace)).toBe(false);
  });

  it('pausing a trace that is not thinking leaves the same object', () => {
    const paused = { text: '想', ms: 3000 };
    expect(pauseThinking(paused, 9000)).toBe(paused);
    expect(pauseThinking(undefined, 9000)).toBeUndefined();
  });

  it('thinking resumed after a tool is a new paragraph and a new span', () => {
    const first = pauseThinking(addThinking(undefined, '要查字典。\n', 0), 2000);
    const resumed = addThinking(first, '查到了。', 5000);
    expect(resumed).toEqual({ text: '要查字典。\n\n查到了。', ms: 2000, since: 5000 });
    expect(pauseThinking(resumed, 6000)?.ms).toBe(3000);
  });

  it('keeps one paragraph break when the server already sent one with the new block', () => {
    const first = pauseThinking(addThinking(undefined, '要查字典。', 0), 1000);
    expect(addThinking(first, '\n\n查到了。', 2000).text).toBe('要查字典。\n\n查到了。');
    // Within a span the server's break between two blocks is kept as it came.
    expect(addThinking(addThinking(undefined, '先找。', 0), '\n\n找到了。', 10).text).toBe(
      '先找。\n\n找到了。',
    );
  });

  it('counts whole seconds, the live span included, at least one', () => {
    expect(thinkingSeconds({ text: '', ms: 200 }, 0)).toBe(1);
    expect(thinkingSeconds({ text: '', ms: 2000, since: 10_000 }, 13_600)).toBe(6);
  });

  it('says thinking while live and how long once done', () => {
    expect(thinkingLabel({ text: '', ms: 0, since: 0 }, 2400)).toBe('思考中… 2 秒');
    expect(thinkingLabel({ text: '', ms: 8400 }, 99_999)).toBe('已思考 8 秒');
  });

  it('previews the last non-empty line, without Markdown marks, cut at a character', () => {
    expect(latestThinkingLine('**先看看**\n\n查到了。\n')).toBe('查到了。');
    expect(latestThinkingLine('')).toBe('');
    const long = '😀'.repeat(90);
    const preview = latestThinkingLine(long);
    expect(Array.from(preview)).toHaveLength(81);
    expect(preview.endsWith('😀…')).toBe(true);
  });
});
