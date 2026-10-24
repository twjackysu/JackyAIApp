import { describe, expect, it } from 'vitest';

import {
  studioChatConfig,
  studioFollowUpsFor as followUpsOn,
  studioShownOn,
  studioSuggestionsFor,
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

describe('studioSuggestionsFor', () => {
  it('starts with the page’s own suggestions', () => {
    expect(studioSuggestionsFor('/english/review')[0].text).toContain('serendipity');
    expect(studioSuggestionsFor('/portfolio')[0].text).toContain('持股');
    expect(studioSuggestionsFor('/connectors')[0].text).toContain('Claude');
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
    expect(learning('/')).toEqual(['帶我去複習', '帶我去每日挑戰']);
    expect(learning('/english/review')).toEqual(['帶我去每日挑戰']);
    expect(learning('/english/daily')).toEqual(['帶我去複習']);
  });
});
