import { describe, expect, it } from 'vitest';

import { FIND_MY_WEAK_SPOTS, READ_DASHBOARD_PROMPT } from './askStudio';
import {
  FIND_MCP_SERVER,
  JIRA_COMMENT_FOLLOW_UP,
  MAKE_DASHBOARD,
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
    expect(studioSuggestionsFor('/dashboards', boards)[1].text).toBe(MAKE_DASHBOARD);
    // None of their own, or a name too long to be found again: making one comes first.
    expect(studioSuggestionsFor('/dashboards', [boards[0]])[0].text).toBe(MAKE_DASHBOARD);
    expect(
      studioSuggestionsFor('/dashboards', [{ name: 'x'.repeat(31), isOwner: true }])[0].text,
    ).toBe(MAKE_DASHBOARD);
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
    // On the connectors page: finding a server in the official MCP Registry (find_mcp_server), then other AIs.
    expect(studioSuggestionsFor('/connectors')[0].text).toBe(FIND_MCP_SERVER);
    expect(studioSuggestionsFor('/connectors')[1].text).toContain('Claude');
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

  it('on the stats and exam pages, offers to find the reader’s weak spots first', () => {
    for (const path of ['/english/stats', '/english/exam', '/english/exam/cloze']) {
      expect(studioSuggestionsFor(path)[0].text).toBe(FIND_MY_WEAK_SPOTS);
    }
    expect(studioSuggestionsFor('/english/review')[0].text).not.toBe(FIND_MY_WEAK_SPOTS);
  });

  it('after reading the weak spots, offers a quiz, an explanation and the review, but not on the review', () => {
    const rule = followUpsOn('/english/stats').find((r) => r.tools?.includes('weak_spots'))!;
    expect(rule.texts).toEqual(['考我最常錯的這幾個字', '解釋我最常錯的那個字', '帶我去複習']);
    const onReview = followUpsOn('/english/review').find((r) => r.tools?.includes('weak_spots'))!;
    expect(onReview.texts).not.toContain('帶我去複習');
  });

  it('after reading Jira, offers a comment on the latest issue; after a comment, the rest of the issues', () => {
    const read = followUpsOn('/').find((r) => r.tools?.includes('jira_issues'))!;
    expect(read.texts).toContain(JIRA_COMMENT_FOLLOW_UP);
    const commented = followUpsOn('/').find((r) => r.tools?.includes('jira_add_comment'))!;
    expect(commented.texts).toEqual(['我手上還有哪些票？']);
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

  it('after keeping or forgetting a note, offers to say what it remembers; and so on the connectors page', () => {
    for (const tool of ['remember', 'forget']) {
      const rule = followUpsOn('/').find((r) => r.tools?.includes(tool))!;
      expect(rule.texts).toEqual(['你記得我什麼？']);
    }
    expect(studioSuggestionsFor('/connectors').map((s) => s.text)).toContain('你記得我什麼？');
    expect(studioChatConfig('/').greeting).toContain('記住');
  });

  it('carries on from a dashboard it read', () => {
    const rule = followUpsOn('/dashboards/x').find((r) => r.tools?.includes('read_dashboard'));
    expect(rule?.texts.length).toBeGreaterThan(0);
  });
});
