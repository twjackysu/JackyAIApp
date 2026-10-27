import { describe, expect, it } from 'vitest';

import { focusAfterDelete } from './focusAfterDelete';

describe('focusAfterDelete (D-187)', () => {
  it('goes to the row that takes the deleted one’s place', () => {
    expect(focusAfterDelete(['a', 'b', 'c'], 'a')).toBe('b');
    expect(focusAfterDelete(['a', 'b', 'c'], 'b')).toBe('c');
  });

  it('goes to the row above when the last one is deleted', () => {
    expect(focusAfterDelete(['a', 'b', 'c'], 'c')).toBe('b');
  });

  it('goes to 新對話 when nothing is left, or the row is not on screen', () => {
    expect(focusAfterDelete(['a'], 'a')).toBeNull();
    expect(focusAfterDelete(['a', 'b'], 'x')).toBeNull();
    expect(focusAfterDelete([], 'a')).toBeNull();
  });
});
