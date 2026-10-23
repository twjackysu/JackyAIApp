import { describe, expect, it } from 'vitest';

import { applyToolEvent, labelFromName, settleToolSteps, type ToolStep } from './toolActivity';

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

  it('closes them as failed when no answer came', () => {
    expect(settleToolSteps(open(), false).map((s) => s.status)).toEqual(['failed', 'failed']);
  });
});
