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

  it('handles quoted commas and escaped quotes for comma separator', () => {
    expect(parseLine('a,"b, c","d""e"', ',')).toEqual(['a', 'b, c', 'd"e']);
  });
});

describe('parseTabular', () => {
  it('returns null when there are fewer than 2 lines', () => {
    expect(parseTabular('只有標題列')).toBeNull();
  });

  it('returns null when fewer than 2 columns are detected', () => {
    expect(parseTabular('col1\nval1')).toBeNull();
  });

  it('parses a header-first CSV blob into header-keyed rows', () => {
    const result = parseTabular('類型,代號\n股票,2330\n現金,');
    expect(result).not.toBeNull();
    expect(result?.headers).toEqual(['類型', '代號']);
    expect(result?.rows).toEqual([
      { 類型: '股票', 代號: '2330' },
      { 類型: '現金', 代號: '' },
    ]);
  });

  it('auto-detects tab separation', () => {
    const result = parseTabular('類型\t代號\n股票\t2330');
    expect(result?.rows).toEqual([{ 類型: '股票', 代號: '2330' }]);
  });
});
