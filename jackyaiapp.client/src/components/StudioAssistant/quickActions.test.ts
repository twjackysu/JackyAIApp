import { describe, expect, it } from 'vitest';

import sitePagesCs from '../../../../JackyAIApp.Server/Services/Assistant/Studio/SitePages.cs?raw';

import { QUICK_PAGES, quickActionsFor } from './quickActions';

const paths = (draft: string, at = '/') => quickActionsFor(draft, at).map((a) => a.path);

describe('quickActionsFor', () => {
  it('offers a page by its name, in Chinese or English, with or without "take me to"', () => {
    expect(paths('複習')).toEqual(['/english/review']);
    expect(paths('帶我去複習')).toEqual(['/english/review']);
    expect(paths('Portfolio')[0]).toBe('/portfolio');
    expect(paths('open dashboards')).toEqual(['/dashboards']);
  });

  it('offers the pages a word starts, the exact one first', () => {
    expect(paths('dash')).toEqual(['/dashboards', '/english/dictionary/dash']);
    expect(paths('資料分析')[0]).toBe('/data-analysis');
  });

  it('offers one English word in the dictionary, after a page it names', () => {
    expect(quickActionsFor('serendipity', '/')).toEqual([
      {
        key: 'word:serendipity',
        label: '查字典：serendipity',
        path: '/english/dictionary/serendipity',
        kind: 'word',
      },
    ]);
    expect(paths('Review')).toEqual(['/english/review', '/english/dictionary/review']);
  });

  it('leaves out the page the reader is on, and the word they are reading', () => {
    expect(paths('複習', '/english/review')).toEqual([]);
    expect(paths('serendipity', '/english/dictionary/serendipity')).toEqual([]);
  });

  it('offers nothing for a question, a sentence, one letter, or nothing', () => {
    expect(paths('幫我看看我的持股，哪幾檔虧最多？')).toEqual([]);
    expect(paths('what does serendipity mean')).toEqual([]);
    expect(paths('d')).toEqual([]);
    expect(paths('   ')).toEqual([]);
    expect(paths('帶我去')).toEqual([]);
    expect(paths('hi')).toEqual([]);
    expect(paths('re')).toEqual([]);
  });

  it('takes a lone go-word as a word, and only strips one followed by a name', () => {
    expect(paths('open')).toEqual(['/english/dictionary/open']);
    expect(paths('opening')).toEqual(['/english/dictionary/opening']);
  });

  it('does not offer the page the reader is already somewhere in', () => {
    expect(paths('英文', '/english/review')).toEqual([]);
  });

  it('offers at most three', () => {
    expect(quickActionsFor('d', '/').length).toBeLessThanOrEqual(3);
    for (const page of QUICK_PAGES)
      for (const word of page.words)
        expect(quickActionsFor(word, '/').length).toBeLessThanOrEqual(3);
  });

  it('sends a typed word to the dictionary as a plain path', () => {
    expect(paths("Don't")).toEqual(["/english/dictionary/don't"]);
    expect(paths('<script>')).toEqual([]);
  });
});

describe('QUICK_PAGES', () => {
  it("has every page of the server's list for Studio AI but the home page", () => {
    const serverPaths = [...sitePagesCs.matchAll(/new\("[^"]+", "([^"]+)"/g)].map((m) => m[1]);
    expect(serverPaths.length).toBeGreaterThan(10);
    expect(QUICK_PAGES.map((p) => p.path).sort()).toEqual(
      serverPaths.filter((p) => p !== '/').sort(),
    );
  });

  it('keeps its words lowercase, so what was typed is matched in any case', () => {
    for (const page of QUICK_PAGES)
      for (const word of page.words) expect(word).toBe(word.toLowerCase());
  });
});
