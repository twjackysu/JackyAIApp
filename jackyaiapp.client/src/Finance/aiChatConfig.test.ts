import { describe, expect, it } from 'vitest';

import { FINANCE_AI_CHAT, FINANCE_TOOL_LABELS } from './aiChatConfig';

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
