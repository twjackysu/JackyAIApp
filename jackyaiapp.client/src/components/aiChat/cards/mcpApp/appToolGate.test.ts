import { describe, expect, it } from 'vitest';

import {
  MAX_WAITING_CALLS,
  OPEN_GATE,
  askingAbout,
  decide,
  hold,
  isAllowed,
  isRefused,
  needsReader,
  shownToolName,
  stopAllowing,
} from './appToolGate';

const call = (id: number, name = 'poll-system-stats') => ({ id, name, arguments: {} });

describe('appToolGate', () => {
  it('holds calls up to the limit and asks about the first one’s tool', () => {
    let gate = OPEN_GATE;
    for (let i = 0; i < MAX_WAITING_CALLS; i++) gate = hold(gate, call(i))!;
    expect(askingAbout(gate)).toBe('poll-system-stats');
    expect(hold(gate, call(99))).toBeNull();
  });

  it('releases only the asked tool’s calls on a decision, and remembers it', () => {
    let gate = hold(hold(hold(OPEN_GATE, call(1))!, call(2, 'other'))!, call(3))!;
    const allowed = decide(gate, true);
    expect(allowed.released.map((c) => c.id)).toEqual([1, 3]);
    expect(isAllowed(allowed.gate, 'poll-system-stats')).toBe(true);
    expect(askingAbout(allowed.gate)).toBe('other');

    gate = decide(allowed.gate, false).gate;
    expect(isRefused(gate, 'other')).toBe(true);
    expect(gate.waiting).toEqual([]);
  });

  it('takes an allowance back as a refusal', () => {
    const gate = decide(hold(OPEN_GATE, call(1))!, true).gate;
    const stopped = stopAllowing(gate, 'poll-system-stats');
    expect(isAllowed(stopped, 'poll-system-stats')).toBe(false);
    expect(isRefused(stopped, 'poll-system-stats')).toBe(true);
  });

  it('a decision with nothing asked changes nothing', () => {
    expect(decide(OPEN_GATE, true)).toEqual({ gate: OPEN_GATE, released: [] });
  });

  it('knows the server’s answer that the call waits on the reader', () => {
    expect(needsReader({ status: 403, data: { error: { code: 'AppCallNeedsReader' } } })).toBe(
      true,
    );
    expect(needsReader({ status: 403, data: { error: { code: 'Forbidden' } } })).toBe(false);
    expect(needsReader(new Error('x'))).toBe(false);
    expect(needsReader(null)).toBe(false);
  });

  it('writes a tool’s name without format characters', () => {
    expect(shownToolName('poll‮stats\u0007')).toBe('pollstats');
    expect(shownToolName('x'.repeat(100))).toHaveLength(60);
  });
});
