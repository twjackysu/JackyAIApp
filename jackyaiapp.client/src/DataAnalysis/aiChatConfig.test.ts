import { describe, expect, it } from 'vitest';

import { DATA_ANALYSIS_AI_CHAT, dataAnalysisAiChatFor } from './aiChatConfig';

const texts = (config: ReturnType<typeof dataAnalysisAiChatFor>) => [
  ...config.suggestions.map((s) => s.text),
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
