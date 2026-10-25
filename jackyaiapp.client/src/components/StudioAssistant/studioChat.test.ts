import { describe, expect, it } from 'vitest';

import { READ_DASHBOARD_PROMPT } from './askStudio';
import {
  MY_HOLDINGS_MARKET,
  studioChatConfig,
  studioFollowUpsFor as followUpsOn,
  studioShownOn,
  dictionaryWordOf,
  studioSuggestionsFor,
  WRITE_WITH_MY_WORDS,
} from './studioChat';

describe('studioShownOn', () => {
  it('is shown on the pages without an assistant of their own', () => {
    expect(studioShownOn('/')).toBe(true);
    expect(studioShownOn('/english/dictionary/apple')).toBe(true);
    expect(studioShownOn('/portfolio')).toBe(true);
    expect(studioShownOn('/financed')).toBe(true);
  });

  it('stays out of the way of a page’s own assistant, and of the slideshow', () => {
    expect(studioShownOn('/finance')).toBe(false);
    expect(studioShownOn('/data-analysis')).toBe(false);
    expect(studioShownOn('/data-analysis/x')).toBe(false);
    expect(studioShownOn('/slideshow')).toBe(false);
    expect(studioShownOn('/oauth/authorize')).toBe(false);
  });
});

describe('dictionaryWordOf', () => {
  it('reads the word of a dictionary page, and nothing else', () => {
    expect(dictionaryWordOf('/english/dictionary/Serendipity')).toBe('serendipity');
    expect(dictionaryWordOf("/english/dictionary/o'clock")).toBe("o'clock");
    expect(dictionaryWordOf('/english/dictionary/o%27clock')).toBe("o'clock");
    expect(dictionaryWordOf('/english/dictionary')).toBeUndefined();
    expect(dictionaryWordOf('/english/dictionary/two%20words')).toBeUndefined();
    expect(dictionaryWordOf('/english/dictionary/%E0%A4%A')).toBeUndefined();
    expect(dictionaryWordOf('/english/dictionary/word/more')).toBeUndefined();
  });
});

describe('studioSuggestionsFor', () => {
  it('on the dashboards list, offers to read the first of the reader’s own', () => {
    const boards = [
      { name: '別人分享的', isOwner: false },
      { name: '業績追蹤', isOwner: true },
    ];
    expect(studioSuggestionsFor('/dashboards', boards)[0].text).toBe(
      '幫我解讀「業績追蹤」這個 dashboard',
    );
    // None of their own, or a name too long to be found again: the site's suggestions.
    expect(studioSuggestionsFor('/dashboards', [boards[0]])[0].text).toBe(
      studioSuggestionsFor('/jobs')[0].text,
    );
    expect(
      studioSuggestionsFor('/dashboards', [{ name: 'x'.repeat(31), isOwner: true }])[0].text,
    ).toBe(studioSuggestionsFor('/jobs')[0].text);
  });

  it('on a word’s dictionary page, offers to do something with that word first', () => {
    const texts = studioSuggestionsFor('/english/dictionary/serendipity').map((s) => s.text);
    expect(texts.slice(0, 2)).toEqual([
      '把 serendipity 加入我的單字庫',
      '用 serendipity 造三個例句，附中文翻譯',
    ]);
    expect(texts).toHaveLength(4);
  });

  it('on the repository, offers to write with the reader’s own words first', () => {
    const texts = studioSuggestionsFor('/english/repository').map((s) => s.text);
    expect(texts[0]).toBe(WRITE_WITH_MY_WORDS);
    expect(texts[1]).toBe('考我今天該複習的單字');
  });

  it('starts with the page’s own suggestions', () => {
    expect(studioSuggestionsFor('/english/review')[0].text).toBe('考我今天該複習的單字');
    expect(studioSuggestionsFor('/portfolio')[0].text).toContain('持股');
    expect(studioSuggestionsFor('/connectors')[0].text).toContain('Claude');
    // On one dashboard: read it. Not on the list of them, where "this dashboard" is none.
    expect(studioSuggestionsFor('/dashboards/8f3c2a10-0000-4000-8000-000000000001')[0].text).toBe(
      READ_DASHBOARD_PROMPT,
    );
    expect(studioSuggestionsFor('/dashboards').map((s) => s.text)).not.toContain(
      READ_DASHBOARD_PROMPT,
    );
  });

  it('has the site’s suggestions everywhere', () => {
    expect(studioSuggestionsFor('/jobs').map((s) => s.text)).toEqual([
      '給我今天的簡報',
      '這個網站可以做哪些事？',
      '我今天有什麼行程？',
      '怎麼讓你使用我自己的 MCP server？',
    ]);
  });
});

describe('studioChatConfig', () => {
  it('is the studio profile, and sends where the reader is', () => {
    const config = studioChatConfig('/english/repository');
    expect(config.profile).toBe('studio');
    expect(config.title).toBe('Studio AI');
    expect(config.pageContext).toEqual({ path: '/english/repository' });
  });

  it('keeps the path short', () => {
    expect(studioChatConfig(`/${'a'.repeat(300)}`).pageContext!.path).toHaveLength(100);
  });
});

describe('studioFollowUpsFor', () => {
  const learning = (path: string) =>
    followUpsOn(path).find((r) => r.tools?.includes('learning_progress'))!.texts;

  it('offers to take the reader to a page, but not to the one they are on', () => {
    expect(learning('/')).toEqual(['考我幾題', '帶我去複習', '帶我去每日挑戰']);
    expect(learning('/english/review')).toEqual(['考我幾題', '帶我去每日挑戰']);
    expect(learning('/english/daily')).toEqual(['考我幾題', '帶我去複習']);
  });

  it('after a quiz, offers another one', () => {
    const quiz = followUpsOn('/').find((r) => r.tools?.includes('review_quiz'))!;
    expect(quiz.texts).toEqual(['再考我 3 題', '帶我去複習']);
  });

  it('after writing with the reader’s words, offers another passage or a quiz', () => {
    const rule = followUpsOn('/english/repository').find((r) => r.tools?.includes('saved_words'))!;
    expect(rule.texts).toEqual(['換個主題再寫一段', '考我幾題']);
  });

  it('on the portfolio page, offers the market side of the holdings, which /finance answers', () => {
    expect(studioSuggestionsFor('/portfolio').map((s) => s.text)).toContain(MY_HOLDINGS_MARKET);
    const rule = followUpsOn('/portfolio').find((r) => r.tools?.includes('portfolio_summary'))!;
    expect(rule.texts).toContain(MY_HOLDINGS_MARKET);
  });

  it('on the portfolio page, offers to record a trade', () => {
    expect(studioSuggestionsFor('/portfolio').map((s) => s.text)).toContain('幫我記一筆今天的交易');
  });

  it('after recording a trade, offers to look at the portfolio as it is now', () => {
    const rule = followUpsOn('/portfolio').find((r) => r.tools?.includes('update_holding'))!;
    expect(rule.texts).toEqual(['我的投資組合現在怎麼樣？']);
  });

  it('carries on from a dashboard it read', () => {
    const rule = followUpsOn('/dashboards/x').find((r) => r.tools?.includes('read_dashboard'));
    expect(rule?.texts.length).toBeGreaterThan(0);
  });
});
