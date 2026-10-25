import { describe, expect, it } from 'vitest';

import {
  FINANCE_AI_CHAT,
  FINANCE_EXAMPLE_QUESTIONS,
  FINANCE_SLASH_COMMANDS,
  FINANCE_TOOL_LABELS,
} from './aiChatConfig';

import { matchSlashCommands } from '@/components/aiChat/utils/slashCommands';

describe('FINANCE_TOOL_LABELS', () => {
  it('names every tool a follow-up question is offered after', () => {
    const tools = (FINANCE_AI_CHAT.followUps ?? []).flatMap((rule) => rule.tools ?? []);
    expect(tools.filter((t) => !FINANCE_TOOL_LABELS[t])).toEqual([]);
  });

  it('gives each tool a short label of its own', () => {
    const labels = Object.values(FINANCE_TOOL_LABELS);
    expect(labels.every((l) => l.trim().length > 0 && l.length <= 12)).toBe(true);
    expect(Object.keys(FINANCE_TOOL_LABELS).every((name) => /^get_[a-z0-9_]+$/.test(name))).toBe(
      true,
    );
  });
});

describe('FINANCE_SLASH_COMMANDS', () => {
  it('asks only the example questions, so within what the tools reach, each once', () => {
    const asks = FINANCE_SLASH_COMMANDS.map((c) => c.ask);
    expect(asks.every((a) => (FINANCE_EXAMPLE_QUESTIONS as readonly string[]).includes(a))).toBe(
      true,
    );
    expect(new Set(asks).size).toBe(asks.length);
    expect(new Set(FINANCE_SLASH_COMMANDS.map((c) => c.key)).size).toBe(asks.length);
    expect(FINANCE_AI_CHAT.slashCommands).toBe(FINANCE_SLASH_COMMANDS);
  });

  it('are found by a word in either language', () => {
    const keys = (draft: string) =>
      matchSlashCommands(draft, FINANCE_SLASH_COMMANDS).map((c) => c.key);
    expect(keys('/外資')).toEqual(['foreign_buying']);
    expect(keys('/景氣')).toEqual(['business_cycle']);
    expect(keys('/futures')).toEqual(['futures_chips']);
    expect(keys('/殖利率')).toEqual(['high_yield']);
  });
});
