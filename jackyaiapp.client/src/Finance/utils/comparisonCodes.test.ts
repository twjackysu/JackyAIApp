import { describe, expect, it } from 'vitest';

import { addComparisonCode, removeComparisonCode, MAX_COMPARISON_CODES } from './comparisonCodes';

describe('addComparisonCode', () => {
  it('appends a trimmed code to the list', () => {
    expect(addComparisonCode(['2330'], '  AAPL  ')).toEqual(['2330', 'AAPL']);
  });

  it('ignores a blank/whitespace-only entry', () => {
    expect(addComparisonCode(['2330'], '   ')).toEqual(['2330']);
  });

  it('dedupes case-insensitively instead of adding a second entry', () => {
    expect(addComparisonCode(['AAPL'], 'aapl')).toEqual(['AAPL']);
  });

  it(`refuses to grow the list past ${MAX_COMPARISON_CODES} entries`, () => {
    const atCap = ['2330', 'AAPL', '0050'];
    expect(addComparisonCode(atCap, 'MSFT')).toBe(atCap);
  });
});

describe('removeComparisonCode', () => {
  it('removes the matching code and leaves the others in order', () => {
    expect(removeComparisonCode(['2330', 'AAPL', '0050'], 'AAPL')).toEqual(['2330', '0050']);
  });

  it('is a no-op when the code is not present', () => {
    const codes = ['2330', 'AAPL'];
    expect(removeComparisonCode(codes, 'MSFT')).toEqual(codes);
  });
});
