import { describe, expect, it } from 'vitest';

import { toWordPath, toWordSlug } from './wordSlug';

describe('toWordSlug', () => {
  it('lowercases the word', () => {
    expect(toWordSlug('Apple')).toBe('apple');
  });

  it('trims surrounding whitespace', () => {
    expect(toWordSlug('  apple  ')).toBe('apple');
  });

  it('URL-encodes a multi-word phrase into one path segment', () => {
    expect(toWordSlug('make up')).toBe('make%20up');
  });
});

describe('toWordPath', () => {
  it('builds the canonical dictionary route for a word', () => {
    expect(toWordPath('Apple')).toBe('/english/dictionary/apple');
  });

  it('URL-encodes a multi-word phrase in the path', () => {
    expect(toWordPath('give up')).toBe('/english/dictionary/give%20up');
  });
});
