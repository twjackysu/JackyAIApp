import { describe, expect, it } from 'vitest';

import sitePromptsCs from '../../../../JackyAIApp.Server/Services/Assistant/Studio/StudioSitePrompts.cs?raw';
import dataAnalysisPromptsCs from '../../../../JackyAIApp.Server/Services/DataAnalysis/Assistant/DataAnalysisSitePrompts.cs?raw';
import englishPromptsCs from '../../../../JackyAIApp.Server/Services/Dictionary/Assistant/EnglishSitePrompts.cs?raw';
import portfolioPromptsCs from '../../../../JackyAIApp.Server/Services/Portfolio/Assistant/PortfolioSitePrompts.cs?raw';

import { BRIEFING_PROMPT, READ_DASHBOARD_PROMPT } from './askStudio';
import { isSlashDraft, pageSlashCommands, SLASH_COMMANDS, slashCommandsFor } from './slashCommands';
import { MAKE_DASHBOARD } from './studioChat';

const keys = (draft: string) => slashCommandsFor(draft).map((c) => c.key);

describe('slashCommandsFor', () => {
  it('lists every command for a bare slash, spaces around it or not', () => {
    expect(keys('/')).toEqual(SLASH_COMMANDS.map((c) => c.key));
    expect(keys('  /  ')).toEqual(SLASH_COMMANDS.map((c) => c.key));
  });

  it('narrows them by label, prompt name or word, in either language', () => {
    expect(keys('/簡報')).toEqual(['daily_briefing']);
    expect(keys('/今日簡報')).toEqual(['daily_briefing']);
    expect(keys('/quiz')).toEqual(['quiz_me']);
    expect(keys('/QUIZ_ME')).toEqual(['quiz_me']);
    expect(keys('/健檢')).toEqual(['portfolio_checkup']);
    expect(keys('/短文')).toEqual(['write_with_my_words']);
    expect(keys('/記得')).toEqual(['memories']);
  });

  it('offers nothing without a leading slash, for no match, or for a sentence', () => {
    expect(keys('簡報')).toEqual([]);
    expect(keys('今天 /簡報')).toEqual([]);
    expect(keys('/xyz123')).toEqual([]);
    expect(keys('/english/dictionary/serendipity/and/more')).toEqual([]);
    expect(keys('/english/review')).toEqual([]);
    expect(keys('/幫我看今天的學習情況')).toEqual([]);
    expect(keys('/a')).toEqual([]);
  });

  it('knows a slash draft', () => {
    expect(isSlashDraft(' /q')).toBe(true);
    expect(isSlashDraft('a/q')).toBe(false);
  });
});

describe('the commands', () => {
  it('sends the briefing as the home page does, in a new conversation; the rest in the one on screen', () => {
    const briefing = SLASH_COMMANDS.find((c) => c.key === 'daily_briefing')!;
    expect(briefing).toMatchObject({ ask: BRIEFING_PROMPT, fresh: true });
    expect(SLASH_COMMANDS.filter((c) => c.fresh).map((c) => c.key)).toEqual(['daily_briefing']);
  });

  it('are keyed uniquely, and those named after the site MCP server’s prompts name real ones', () => {
    expect(new Set(SLASH_COMMANDS.map((c) => c.key)).size).toBe(SLASH_COMMANDS.length);
    const server = sitePromptsCs + englishPromptsCs + portfolioPromptsCs;
    for (const key of ['daily_briefing', 'quiz_me', 'write_with_my_words', 'portfolio_checkup']) {
      expect(SLASH_COMMANDS.some((c) => c.key === key)).toBe(true);
      expect(server).toContain(`"${key}"`);
    }
  });
});

describe('the page’s own commands', () => {
  const ID = '6F9619FF-8B86-D011-B42D-00C04FC964FF';
  const own = [{ id: ID, isOwner: true }];
  const shared = [{ id: ID, isOwner: false }];
  const pageKeys = (draft: string, path: string, boards = own) =>
    slashCommandsFor(draft, path, boards).map((c) => c.key);

  it('explains the word of a dictionary page, first', () => {
    expect(pageKeys('/', '/english/dictionary/serendipity')[0]).toBe('explain_word');
    const [explain] = pageSlashCommands('/english/dictionary/Serendipity');
    expect(explain.label).toBe('解釋 serendipity');
    expect(explain.ask).toContain('serendipity');
    expect(explain.fresh).toBeUndefined();
    expect(pageKeys('/解釋', '/english/dictionary/serendipity')).toEqual(['explain_word']);
    expect(pageKeys('/seren', '/english/dictionary/serendipity')).toEqual(['explain_word']);
  });

  it('reads the dashboard on screen only when it is the reader’s own', () => {
    const path = `/dashboards/${ID.toLowerCase()}`;
    expect(pageKeys('/', path)[0]).toBe('read_my_dashboard');
    expect(pageKeys('/解讀', path)).toEqual(['read_my_dashboard']);
    expect(pageSlashCommands(path, own)[0]).toMatchObject({
      ask: READ_DASHBOARD_PROMPT,
      fresh: true,
    });
    expect(pageSlashCommands(`/dashboards/${encodeURIComponent(ID)}`, own)).toHaveLength(1);
    expect(pageSlashCommands(path, shared)).toEqual([]);
    expect(pageSlashCommands(path, [])).toEqual([]);
    expect(pageSlashCommands(`/dashboards/${ID}/edit`, own)).toEqual([]);
    expect(pageSlashCommands('/dashboards', own).map((c) => c.key)).toEqual(['make_dashboard']);
  });

  it('makes a new dashboard from the dashboards list, as its suggestion does', () => {
    const [make] = pageSlashCommands('/dashboards');
    expect(make).toMatchObject({ key: 'make_dashboard', ask: MAKE_DASHBOARD });
    expect(pageKeys('/做', '/dashboards')).toEqual(['make_dashboard']);
  });

  it('adds nothing on other pages, and keeps the site’s commands after the page’s', () => {
    expect(pageKeys('/', '/portfolio')).toEqual(SLASH_COMMANDS.map((c) => c.key));
    expect(pageKeys('/', '/english/dictionary/serendipity').slice(1)).toEqual(
      SLASH_COMMANDS.map((c) => c.key),
    );
    expect(pageKeys('/xyz123', '/english/dictionary/serendipity')).toEqual([]);
  });

  it('are named after the site MCP server’s prompts for the same request', () => {
    expect(englishPromptsCs).toContain('"explain_word"');
    expect(dataAnalysisPromptsCs).toContain('"read_my_dashboard"');
  });
});
