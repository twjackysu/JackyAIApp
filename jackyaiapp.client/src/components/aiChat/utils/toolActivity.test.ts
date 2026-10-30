import { describe, expect, it } from 'vitest';

import {
  applyToolEvent,
  labelFromName,
  settleToolSteps,
  toolDurationText,
  type ToolStep,
} from './toolActivity';

describe('labelFromName', () => {
  it('drops the get_ prefix and the underscores', () => {
    expect(labelFromName('get_stock_history')).toBe('stock history');
  });

  it("drops a connector's key prefix", () => {
    expect(labelFromName('c3__search_issues')).toBe('search issues');
  });

  it('keeps a name it cannot make anything of', () => {
    expect(labelFromName('get_')).toBe('get_');
  });
});

describe('applyToolEvent', () => {
  it('adds a running step for each call, in order, keyed apart even for the same tool', () => {
    let steps: ToolStep[] = [];
    steps = applyToolEvent(steps, { type: 'tool_use', name: 'get_realtime_quote' });
    steps = applyToolEvent(steps, { type: 'tool_use', name: 'get_realtime_quote' });

    expect(steps.map((s) => s.status)).toEqual(['running', 'running']);
    expect(new Set(steps.map((s) => s.key)).size).toBe(2);
    expect(steps[0].label).toBe('realtime quote');
  });

  it("uses the server's display name when it sends one", () => {
    const steps = applyToolEvent([], {
      type: 'tool_use',
      name: 'da__summarize',
      id: 't1',
      displayName: '彙總資料',
    });

    expect(steps[0]).toMatchObject({ id: 't1', key: 't1', label: '彙總資料' });
  });

  it('a result closes the running step with its id, as done or failed', () => {
    let steps = applyToolEvent([], { type: 'tool_use', name: 'a', id: 't1' });
    steps = applyToolEvent(steps, { type: 'tool_use', name: 'b', id: 't2' });
    steps = applyToolEvent(steps, {
      type: 'tool_result',
      id: 't2',
      name: 'b',
      ok: false,
      durationMs: 40,
    });
    steps = applyToolEvent(steps, { type: 'tool_result', id: 't1', name: 'a', ok: true });

    expect(steps.map((s) => s.status)).toEqual(['done', 'failed']);
    expect(steps[1].durationMs).toBe(40);
  });

  it("keeps a successful result's card, checked", () => {
    const steps = applyToolEvent(applyToolEvent([], { type: 'tool_use', name: 'a', id: 't1' }), {
      type: 'tool_result',
      id: 't1',
      name: 'a',
      ok: true,
      card: { kind: 'link', title: '單字', href: '/english/dictionary/x' },
    });
    expect(steps[0].card).toMatchObject({ kind: 'link', href: '/english/dictionary/x' });
  });

  it('shows a failed call only its link to a page of this site (its way forward)', () => {
    const use = applyToolEvent([], { type: 'tool_use', name: 'a', id: 't1' });
    const failed = (card: unknown) =>
      applyToolEvent(use, { type: 'tool_result', id: 't1', name: 'a', ok: false, card })[0].card;
    expect(
      failed({ kind: 'link', title: '重新連線', href: '/connectors?connector=c1' }),
    ).toMatchObject({ kind: 'link', href: '/connectors?connector=c1' });
    expect(failed({ kind: 'link', title: 'x', href: 'https://example.com/x' })).toBeUndefined();
    expect(failed({ kind: 'link', title: 'x', href: '//evil.example/x' })).toBeUndefined();
    expect(
      failed({ kind: 'table', columns: [{ key: 'a', label: 'a' }], rows: [{ a: 1 }] }),
    ).toBeUndefined();
    expect(failed({ kind: 'chart', data: {} })).toBeUndefined();
  });

  it('shows no card that cannot be drawn safely', () => {
    const use = applyToolEvent([], { type: 'tool_use', name: 'a', id: 't1' });
    const unsafe = { kind: 'link', title: 'x', href: 'javascript:alert(1)' };
    expect(
      applyToolEvent(use, { type: 'tool_result', id: 't1', name: 'a', ok: true, card: unsafe })[0]
        .card,
    ).toBeUndefined();
  });

  it('a result for no running step changes nothing', () => {
    const steps = applyToolEvent([], { type: 'tool_use', name: 'a', id: 't1' });

    expect(applyToolEvent(steps, { type: 'tool_result', id: 'nope', name: 'a', ok: true })).toEqual(
      steps,
    );
  });
});

describe('settleToolSteps', () => {
  const open = (): ToolStep[] => {
    let steps = applyToolEvent([], { type: 'tool_use', name: 'a', id: 't1' });
    steps = applyToolEvent(steps, { type: 'tool_result', id: 't1', name: 'a', ok: false });
    return applyToolEvent(steps, { type: 'tool_use', name: 'get_stock_history' });
  };

  it('closes the steps still running as done once the answer came', () => {
    expect(settleToolSteps(open(), true).map((s) => s.status)).toEqual(['failed', 'done']);
  });

  it('closes one of our own tools still waiting as failed, even after a stop that kept the answer', () => {
    // The reader stopped while a write waited for their yes: the server ran nothing.
    let steps = applyToolEvent([], { type: 'tool_use', name: 'update_holding', id: 't9' });
    steps = applyToolEvent(steps, { type: 'tool_use', name: 'get_stock_history' });
    expect(settleToolSteps(steps, true).map((s) => s.status)).toEqual(['failed', 'done']);
  });

  it('closes them as failed when no answer came', () => {
    expect(settleToolSteps(open(), false).map((s) => s.status)).toEqual(['failed', 'failed']);
  });
});

describe('toolDurationText', () => {
  it('says tenths of a second under ten seconds and whole seconds from there', () => {
    expect(toolDurationText(812)).toBe('0.8 秒');
    expect(toolDurationText(9_940)).toBe('9.9 秒');
    expect(toolDurationText(9_950)).toBe('10 秒');
    expect(toolDurationText(61_400)).toBe('61 秒');
  });

  it('says nothing for a call not timed or too quick to matter', () => {
    expect(toolDurationText(undefined)).toBeNull();
    expect(toolDurationText(99)).toBeNull();
    expect(toolDurationText(Number.NaN)).toBeNull();
    expect(toolDurationText(100)).toBe('0.1 秒');
  });
});
