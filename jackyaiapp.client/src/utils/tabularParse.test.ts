import { describe, expect, it } from 'vitest';

import { detectSep, parseLine, parseTabular } from './tabularParse';

describe('detectSep', () => {
  it('picks tab when the first line has at least as many tabs as commas', () => {
    expect(detectSep('a\tb\tc')).toBe('\t');
  });

  it('picks comma when commas outnumber tabs', () => {
    expect(detectSep('a,b,c')).toBe(',');
  });
});

describe('parseLine', () => {
  it('splits on a non-comma separator without quote handling', () => {
    expect(parseLine('a\tb\tc', '\t')).toEqual(['a', 'b', 'c']);
  });

  it('handles quoted commas and escaped double-quotes for comma-separated lines', () => {
    expect(parseLine('a,"b, still b","c ""quoted"""', ',')).toEqual([
      'a',
      'b, still b',
      'c "quoted"',
    ]);
  });
});

describe('parseTabular', () => {
  it('returns null when there is no data row', () => {
    expect(parseTabular('只有標題列')).toBeNull();
  });

  it('returns null when the header row has fewer than 2 columns', () => {
    expect(parseTabular('single\nvalue')).toBeNull();
  });

  it('parses a header-keyed comma-separated table', () => {
    const result = parseTabular('Date,Asset,Value\n2025-01,Stocks,100000');
    expect(result?.headers).toEqual(['Date', 'Asset', 'Value']);
    expect(result?.rows).toEqual([{ Date: '2025-01', Asset: 'Stocks', Value: '100000' }]);
  });

  it('skips blank lines', () => {
    const result = parseTabular('A,B\n1,2\n\n3,4');
    expect(result?.rows).toHaveLength(2);
  });
});
