import { describe, expect, it } from 'vitest';

import { detectSep, parseLine, parseTabular } from './tabularParse';

describe('detectSep', () => {
  it('picks tab when the header row has at least as many tabs as commas', () => {
    expect(detectSep('a\tb\tc')).toBe('\t');
  });

  it('picks comma when there are strictly more commas than tabs', () => {
    expect(detectSep('a,b,c')).toBe(',');
  });

  it('defaults to tab when there are no separators at all (tabs >= commas at 0 = 0)', () => {
    expect(detectSep('a')).toBe('\t');
  });
});

describe('parseLine', () => {
  it('splits on a non-comma separator with no quote handling', () => {
    expect(parseLine('a\tb\t c ', '\t')).toEqual(['a', 'b', 'c']);
  });

  it('handles quoted commas and escaped quotes for comma-separated lines', () => {
    expect(parseLine('a,"b, c",d', ',')).toEqual(['a', 'b, c', 'd']);
    expect(parseLine('a,"He said ""hi""",b', ',')).toEqual(['a', 'He said "hi"', 'b']);
  });
});

describe('parseTabular', () => {
  it('returns null with fewer than 2 lines or fewer than 2 columns', () => {
    expect(parseTabular('只有標題列')).toBeNull();
    expect(parseTabular('single\nvalue')).toBeNull();
  });

  it('parses a header + data rows into header-keyed records', () => {
    const result = parseTabular('a,b\n1,2\n3,4');
    expect(result?.headers).toEqual(['a', 'b']);
    expect(result?.rows).toEqual([
      { a: '1', b: '2' },
      { a: '3', b: '4' },
    ]);
  });

  it('fills a short row with empty strings for missing trailing columns', () => {
    const result = parseTabular('a,b,c\n1,2');
    expect(result?.rows).toEqual([{ a: '1', b: '2', c: '' }]);
  });
});
