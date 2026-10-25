import { describe, expect, it } from 'vitest';

import { isSlashDraft, matchSlashCommands, type SlashCommand } from './slashCommands';

const COMMANDS: readonly SlashCommand[] = [
  { key: 'market_news', label: '今日重大消息', ask: 'a', kind: 'command', words: ['news'] },
  { key: 'trend', label: '近 12 個月趨勢', ask: 'b', kind: 'command', words: ['趨勢', 'chart'] },
];
const keys = (draft: string) => matchSlashCommands(draft, COMMANDS).map((c) => c.key);

describe('matchSlashCommands', () => {
  it('lists all of an assistant’s commands for a bare slash, in its order', () => {
    expect(keys('/')).toEqual(['market_news', 'trend']);
    expect(keys(' / ')).toEqual(['market_news', 'trend']);
  });

  it('narrows by label at once, and by key or word from two characters', () => {
    expect(keys('/消')).toEqual(['market_news']);
    expect(keys('/ne')).toEqual(['market_news']);
    expect(keys('/n')).toEqual([]);
    expect(keys('/CHART')).toEqual(['trend']);
    expect(keys('/market')).toEqual(['market_news']);
  });

  it('offers nothing without a leading slash, for a path, or for a sentence', () => {
    expect(keys('趨勢')).toEqual([]);
    expect(keys('/finance/x')).toEqual([]);
    expect(keys('/幫我畫出近十二個月每個地區的營收趨勢，並比較去年')).toEqual([]);
    expect(isSlashDraft('  /x')).toBe(true);
    expect(isSlashDraft('x/')).toBe(false);
  });

  it('returns a list of its own, never the assistant’s', () => {
    expect(matchSlashCommands('/', COMMANDS)).not.toBe(COMMANDS);
  });
});
