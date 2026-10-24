import { describe, expect, it } from 'vitest';

import {
  alwaysAllowRequest,
  canAlwaysAllow,
  decisionsFor,
  formatArgument,
  type ApprovalCall,
} from './approval';

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

describe('canAlwaysAllow', () => {
  const siteWrite = (patch: Partial<ApprovalCall>): ApprovalCall => ({
    toolUseId: 't',
    connector: 'site',
    tool: 'add_word_to_repository',
    displayName: '加入單字庫',
    effect: 'write',
    input: {},
    ...patch,
  });

  it('is offered for a site tool’s write, as the connectors page would set it', () => {
    expect(canAlwaysAllow(siteWrite({}))).toBe(true);
    expect(alwaysAllowRequest(siteWrite({}))).toEqual({
      definitionKey: 'site',
      installationId: null,
      toolName: 'add_word_to_repository',
      permission: 'auto',
    });
  });

  it('is not for what can never run on its own, an outside connector, or after outside content', () => {
    expect(canAlwaysAllow(siteWrite({ effect: 'destructive' }))).toBe(false);
    expect(canAlwaysAllow(siteWrite({ connector: 'inst-1', tool: 'm1__create' }))).toBe(false);
    expect(canAlwaysAllow(siteWrite({}), true)).toBe(false);
  });
});
