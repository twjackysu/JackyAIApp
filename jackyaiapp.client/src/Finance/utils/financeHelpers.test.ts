import { describe, expect, it } from 'vitest';

import { addComparisonCode, MAX_COMPARISON_CODES } from './financeHelpers';

describe('addComparisonCode', () => {
  it('adds a trimmed code to an empty list', () => {
    expect(addComparisonCode([], '  2330  ')).toEqual(['2330']);
  });

  it('appends to an existing list', () => {
    expect(addComparisonCode(['2330'], 'AAPL')).toEqual(['2330', 'AAPL']);
  });

  it(`caps at ${MAX_COMPARISON_CODES} entries`, () => {
    const atLimit = ['2330', 'AAPL', '2454'];
    expect(atLimit.length).toBe(MAX_COMPARISON_CODES);
    expect(addComparisonCode(atLimit, 'TSLA')).toEqual(atLimit);
  });

  it('is case-insensitively de-duplicated', () => {
    expect(addComparisonCode(['aapl'], 'AAPL')).toEqual(['aapl']);
  });

  it('ignores blank input', () => {
    expect(addComparisonCode(['2330'], '   ')).toEqual(['2330']);
  });
});
