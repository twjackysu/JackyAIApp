import { describe, expect, it } from 'vitest';

import {
  DATA_ANALYSIS_AI_CHAT,
  DATA_ANALYSIS_SLASH_COMMANDS,
  dataAnalysisAiChatFor,
  MAKE_OVERVIEW_DASHBOARD,
} from './aiChatConfig';

import { MAKE_DASHBOARD } from '@/components/StudioAssistant/studioChat';

const texts = (config: ReturnType<typeof dataAnalysisAiChatFor>) => [
  ...config.suggestions.map((s) => s.text),
  ...(config.slashCommands ?? []).flatMap((c) => [c.label, c.ask]),
  ...(config.followUps ?? []).flatMap((r) => r.texts),
  config.greeting,
];

describe('dataAnalysisAiChatFor', () => {
  it('is the assistant as it is for a data source', () => {
    expect(dataAnalysisAiChatFor(false)).toBe(DATA_ANALYSIS_AI_CHAT);
    expect(texts(DATA_ANALYSIS_AI_CHAT).some((t) => t.includes('dashboard'))).toBe(true);
  });

  it('offers nothing about dashboards for an uploaded file, whose charts cannot be saved', () => {
    const config = dataAnalysisAiChatFor(true);
    expect(texts(config).some((t) => t.includes('dashboard'))).toBe(false);
    expect(config.suggestions.length).toBeGreaterThanOrEqual(3);
    expect(config.followUps?.some((r) => r.tools?.includes('ask_data'))).toBe(true);
    expect(config.profile).toBe('data-analysis');
  });
});

describe('the ready-made requests (/)', () => {
  it('are keyed uniquely, and making a dashboard is the sentence Studio AI hands over (D-095)', () => {
    const keys = DATA_ANALYSIS_SLASH_COMMANDS.map((c) => c.key);
    expect(new Set(keys).size).toBe(keys.length);
    expect(DATA_ANALYSIS_SLASH_COMMANDS.find((c) => c.key === 'make_dashboard')?.ask).toBe(
      MAKE_OVERVIEW_DASHBOARD,
    );
    expect(MAKE_OVERVIEW_DASHBOARD).toBe(MAKE_DASHBOARD);
  });

  it('keep the rest for an uploaded file', () => {
    expect(dataAnalysisAiChatFor(true).slashCommands?.map((c) => c.key)).toEqual([
      'describe_data',
      'highlights',
      'trend',
    ]);
  });
});
