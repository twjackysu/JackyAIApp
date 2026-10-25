import { describe, expect, it } from 'vitest';

import sitePromptsCs from '../../../../JackyAIApp.Server/Services/Assistant/Studio/StudioSitePrompts.cs?raw';
import englishPromptsCs from '../../../../JackyAIApp.Server/Services/Dictionary/Assistant/EnglishSitePrompts.cs?raw';
import portfolioPromptsCs from '../../../../JackyAIApp.Server/Services/Portfolio/Assistant/PortfolioSitePrompts.cs?raw';

import { BRIEFING_PROMPT } from './askStudio';
import { isSlashDraft, SLASH_COMMANDS, slashCommandsFor } from './slashCommands';

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
