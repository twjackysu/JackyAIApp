import { describe, expect, it } from 'vitest';

import { decisionsFor, formatArgument, type ApprovalCall } from './approval';

const call = (toolUseId: string): ApprovalCall => ({
  toolUseId,
  connector: 'site',
  tool: 'add_word',
  displayName: '加入單字',
  effect: 'write',
  input: {},
});

describe('formatArgument', () => {
  it('shows text as it is and anything else as compact JSON', () => {
    expect(formatArgument('serendipity')).toBe('serendipity');
    expect(formatArgument(3)).toBe('3');
    expect(formatArgument({ a: [1, 2] })).toBe('{"a":[1,2]}');
  });

  it('cuts a long value', () => {
    const shown = formatArgument('x'.repeat(500));
    expect(shown).toHaveLength(120);
    expect(shown.endsWith('…')).toBe(true);
  });

  it('copes with a value JSON cannot write', () => {
    expect(formatArgument(undefined)).toBe('undefined');
  });
});

describe('decisionsFor', () => {
  it('gives one decision per call, an undecided one counted as a no', () => {
    expect(decisionsFor([call('a'), call('b'), call('c')], { a: true, b: false })).toEqual([
      { toolUseId: 'a', approve: true },
      { toolUseId: 'b', approve: false },
      { toolUseId: 'c', approve: false },
    ]);
  });
});
