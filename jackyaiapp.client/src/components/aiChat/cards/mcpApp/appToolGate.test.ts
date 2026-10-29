import { describe, expect, it } from 'vitest';

import {
  MAX_WAITING_CALLS,
  OPEN_GATE,
  askingAbout,
  decide,
  forNewDocument,
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

  it('asks about one tool at a time: another tool’s call is not held behind it', () => {
    const gate = hold(OPEN_GATE, call(1))!;
    expect(hold(gate, call(2, 'other'))).toBeNull();
  });

  it('releases the asked tool’s calls on a decision about it, and remembers it', () => {
    const gate = hold(hold(OPEN_GATE, call(1))!, call(3))!;
    const allowed = decide(gate, 'poll-system-stats', true);
    expect(allowed.released.map((c) => c.id)).toEqual([1, 3]);
    expect(isAllowed(allowed.gate, 'poll-system-stats')).toBe(true);
    expect(askingAbout(allowed.gate)).toBeNull();

    const refused = decide(hold(allowed.gate, call(4, 'other'))!, 'other', false).gate;
    expect(isRefused(refused, 'other')).toBe(true);
    expect(refused.waiting).toEqual([]);
  });

  it('a decision about a tool the card is no longer asking about changes nothing', () => {
    const gate = hold(OPEN_GATE, call(1))!;
    expect(decide(gate, 'other', true)).toEqual({ gate, released: [] });
  });

  it('drops the old document’s calls but keeps what the reader decided', () => {
    const decided = decide(hold(OPEN_GATE, call(1))!, 'poll-system-stats', true).gate;
    const again = forNewDocument(hold(stopAllowing(decided, 'x'), call(2, 'y'))!);
    expect(again.waiting).toEqual([]);
    expect(isAllowed(again, 'poll-system-stats')).toBe(true);
  });

  it('takes an allowance back as a refusal', () => {
    const gate = decide(hold(OPEN_GATE, call(1))!, 'poll-system-stats', true).gate;
    const stopped = stopAllowing(gate, 'poll-system-stats');
    expect(isAllowed(stopped, 'poll-system-stats')).toBe(false);
    expect(isRefused(stopped, 'poll-system-stats')).toBe(true);
  });

  it('a decision with nothing asked changes nothing', () => {
    expect(decide(OPEN_GATE, 'x', true)).toEqual({ gate: OPEN_GATE, released: [] });
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
