import { describe, expect, it } from 'vitest';

import { wordPath } from './wordPath';

describe('wordPath', () => {
  it('lowercases the word to match the backend lookup key', () => {
    expect(wordPath('Apple')).toBe('/english/dictionary/apple');
  });

  it('trims surrounding whitespace', () => {
    expect(wordPath('  apple  ')).toBe('/english/dictionary/apple');
  });

  it('URL-encodes multi-word phrases into a single path segment', () => {
    expect(wordPath('give up')).toBe('/english/dictionary/give%20up');
  });
});
