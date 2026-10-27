import { describe, expect, it } from 'vitest';

import jobAggregatorCs from '../../../JackyAIApp.Server/Services/Job/JobAggregatorService.cs?raw';

import { MAX_KEYWORDS_LENGTH } from './limits';

describe('job search limits', () => {
  it('holds the search box to the length the server takes', () => {
    expect(jobAggregatorCs).toContain(
      `public const int MaxKeywordsLength = ${MAX_KEYWORDS_LENGTH};`,
    );
  });
});
