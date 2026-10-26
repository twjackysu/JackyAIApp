import { describe, expect, it } from 'vitest';

import { stillSpoken } from './spokenDraft';

describe('stillSpoken', () => {
  it('counts a draft that is what was heard, or that and what was typed before', () => {
    expect(stillSpoken('serendipity 是什麼', 'serendipity 是什麼')).toBe(true);
    expect(stillSpoken('幫我查單字', '單字')).toBe(true);
  });

  it('keeps counting a misheard word the reader fixed', () => {
    expect(stillSpoken('serendipity 是什麼意思', 'sarah dippity 是什麼意思')).toBe(true);
  });

  it('stops counting once what was heard is deleted and something else typed', () => {
    expect(stillSpoken('幫我記一筆交易', '今天天氣如何')).toBe(false);
    expect(stillSpoken('幫我查', '幫我查單字 serendipity')).toBe(false);
  });

  it('counts nothing when nothing was heard', () => {
    expect(stillSpoken('你好', '')).toBe(false);
    expect(stillSpoken('你好', '   ')).toBe(false);
  });
});
