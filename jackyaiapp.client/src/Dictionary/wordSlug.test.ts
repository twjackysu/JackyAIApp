import { describe, expect, it } from 'vitest';

import { toWordSlug } from './wordSlug';

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
