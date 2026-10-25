import { describe, expect, it } from 'vitest';

import { HANDOFF_MAX, handoffFromState, handoffOf, handoffState } from './handoff';

describe('handoffOf', () => {
  it('keeps a question, trimmed', () => {
    expect(handoffOf('  幫我做一個業績總覽 dashboard ')).toBe('幫我做一個業績總覽 dashboard');
  });

  it('is nothing for blank or anything that is not text', () => {
    expect(handoffOf('   ')).toBeUndefined();
    expect(handoffOf(null)).toBeUndefined();
    expect(handoffOf(42)).toBeUndefined();
    expect(handoffOf({ text: 'x' })).toBeUndefined();
  });

  it('cuts a long one to the limit', () => {
    expect(handoffOf('問'.repeat(HANDOFF_MAX + 10))).toHaveLength(HANDOFF_MAX);
  });
});

describe('handoffFromState', () => {
  it('reads back what handoffState put there', () => {
    expect(handoffFromState(handoffState('台積電最近外資在買還是賣？'))).toBe(
      '台積電最近外資在買還是賣？',
    );
  });

  it('is nothing for another state', () => {
    expect(handoffFromState(null)).toBeUndefined();
    expect(handoffFromState('assistantHandoff')).toBeUndefined();
    expect(handoffFromState({ from: '/finance' })).toBeUndefined();
    expect(handoffFromState({ assistantHandoff: '  ' })).toBeUndefined();
  });
});
