import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import ApprovalCard from './ApprovalCard';

import type { ApprovalCall, PendingApproval } from '../utils/approval';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

const call = (toolUseId: string, patch: Partial<ApprovalCall> = {}): ApprovalCall => ({
  toolUseId,
  connector: 'site',
  connectorName: '本站・單字庫',
  tool: 'add_word',
  displayName: '加入單字',
  effect: 'write',
  input: { word: 'serendipity' },
  ...patch,
});

const buttons = () => [...container.querySelectorAll('button')];
const button = (text: string) => buttons().find((b) => b.textContent === text)!;
const click = (el: HTMLElement) => act(() => el.click());

function render(
  approval: PendingApproval,
  onSubmit = vi.fn(),
  state: 'pending' | 'sending' | 'resolved' | 'failed' = 'pending',
  decisions?: { toolUseId: string; approve: boolean }[],
) {
  act(() =>
    root.render(
      <ApprovalCard approval={approval} state={state} decisions={decisions} onSubmit={onSubmit} />,
    ),
  );
  return onSubmit;
}

describe('ApprovalCard', () => {
  it('shows what the call would do and send', () => {
    render({ pendingId: 'p', calls: [call('a')] });

    expect(container.textContent).toContain('需要你的確認');
    expect(container.textContent).toContain('本站・單字庫');
    expect(container.textContent).toContain('加入單字');
    expect(container.textContent).toContain('word:serendipity');
    // The site's own tool runs here: no "on ○○" line.
    expect(container.textContent).not.toContain('上執行');
  });

  it('says in words what a call would do when its tool says, its arguments behind a toggle', () => {
    render({
      pendingId: 'p',
      calls: [
        call('a', { summary: '把「圖」存到 dashboard「業績」', input: { chartId: 'chart-k3f9' } }),
        call('b'),
      ],
    });

    const summaries = [...container.querySelectorAll('[data-testid="approval-summary"]')];
    expect(summaries.map((s) => s.textContent)).toEqual(['把「圖」存到 dashboard「業績」']);
    // The described call keeps its ids out of sight; the one without words shows what it sends.
    expect(container.textContent).not.toContain('chart-k3f9');
    expect(container.textContent).toContain('word:serendipity');

    const toggle = button('顯示送出的參數');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    click(toggle);
    expect(container.textContent).toContain('chartId:chart-k3f9');
    click(button('隱藏送出的參數'));
    expect(container.textContent).not.toContain('chart-k3f9');
  });

  it('offers no toggle for a described call with nothing to send', () => {
    render({ pendingId: 'p', calls: [call('a', { summary: '做一件事', input: {} })] });
    expect(buttons().some((b) => b.textContent === '顯示送出的參數')).toBe(false);
  });

  it('one call is allowed or refused straight away', () => {
    const allow = render({ pendingId: 'p', calls: [call('a')] });
    click(button('允許'));
    expect(allow).toHaveBeenCalledWith([{ toolUseId: 'a', approve: true }]);

    const refuse = render({ pendingId: 'p', calls: [call('a')] }, vi.fn());
    click(button('拒絕'));
    expect(refuse).toHaveBeenCalledWith([{ toolUseId: 'a', approve: false }]);
  });

  it('several calls are decided one by one and sent together, once all are decided', () => {
    const onSubmit = render({
      pendingId: 'p',
      calls: [call('a'), call('b', { displayName: '建立 issue' })],
    });
    const send = () => button('照上面的選擇送出');

    expect(send().disabled).toBe(true);
    expect(container.textContent).toContain('也可以逐項選好允許或拒絕');

    const [allowA, , , refuseB] = buttons().filter(
      (b) => b.textContent === '允許' || b.textContent === '拒絕',
    );
    click(allowA);
    expect(send().disabled).toBe(true);
    click(refuseB);
    expect(send().disabled).toBe(false);

    click(send());
    expect(onSubmit).toHaveBeenCalledWith([
      { toolUseId: 'a', approve: true },
      { toolUseId: 'b', approve: false },
    ]);
  });

  it("names where an outside connector's action happens, and warns about one that cannot be undone", () => {
    render({
      pendingId: 'p',
      calls: [call('a', { connector: 'jira', connectorName: 'Jira', effect: 'destructive' })],
      tainted: true,
    });

    expect(container.textContent).toContain('這個動作會在 Jira 上執行');
    expect(container.textContent).toContain('無法復原');
    expect(container.textContent).toContain('這段對話讀過外部內容，寫入動作一律需要確認。');
  });

  it('once answered, keeps what was decided in place of the buttons', () => {
    render({ pendingId: 'p', calls: [call('a'), call('b')] }, vi.fn(), 'resolved', [
      { toolUseId: 'a', approve: true },
      { toolUseId: 'b', approve: false },
    ]);

    expect(buttons()).toHaveLength(0);
    expect(container.textContent).toContain('已允許');
    expect(container.textContent).toContain('已拒絕');
  });

  it('a decision that did not arrive can be sent again', () => {
    render({ pendingId: 'p', calls: [call('a')] }, vi.fn(), 'failed');

    expect(container.textContent).toContain('沒有送達，請再試一次。');
    expect(button('允許')).toBeDefined();
  });

  it('several calls can be allowed, or refused, all at once', () => {
    const calls = [
      call('a'),
      call('b'),
      call('c', { tool: 'save', displayName: '存到 dashboard' }),
    ];
    const onSubmit = render({ pendingId: 'p', calls });

    click(button('全部允許（3）'));
    expect(onSubmit).toHaveBeenCalledWith([
      { toolUseId: 'a', approve: true },
      { toolUseId: 'b', approve: true },
      { toolUseId: 'c', approve: true },
    ]);

    const refused = render({ pendingId: 'q', calls });
    click(button('全部拒絕'));
    expect(refused).toHaveBeenCalledWith([
      { toolUseId: 'a', approve: false },
      { toolUseId: 'b', approve: false },
      { toolUseId: 'c', approve: false },
    ]);
  });

  it('offers no 全部允許 when one call cannot be undone, is an outside connector’s, or after outside content', () => {
    render({ pendingId: 'p', calls: [call('a'), call('b', { effect: 'destructive' })] });
    expect(buttons().some((b) => b.textContent?.startsWith('全部允許'))).toBe(false);
    expect(button('全部拒絕')).toBeDefined();
    expect(button('送出').disabled).toBe(true);
    expect(container.textContent).toContain('每一項都選好允許或拒絕後才能送出');

    render({ pendingId: 'q', calls: [call('c'), call('d')], tainted: true });
    expect(buttons().some((b) => b.textContent?.startsWith('全部允許'))).toBe(false);
    expect(button('送出')).toBeDefined();

    // A note Studio AI would remember lasts into every later prompt: always its own yes (D-101).
    render({ pendingId: 's', calls: [call('g'), call('h', { alwaysAsks: true })] });
    expect(buttons().some((b) => b.textContent?.startsWith('全部允許'))).toBe(false);

    // An outside connector's tool says its own effect (a hint it may leave out): decided one by one.
    render({ pendingId: 'r', calls: [call('e'), call('f', { connector: 'inst-1' })] });
    expect(buttons().some((b) => b.textContent?.startsWith('全部允許'))).toBe(false);
  });

  describe('letting a site tool run on its own from now on', () => {
    const checkbox = () => container.querySelector<HTMLInputElement>('input[type="checkbox"]');

    function renderWith(approval: PendingApproval) {
      const onSubmit = vi.fn();
      const onAlwaysAllow = vi.fn();
      act(() =>
        root.render(
          <ApprovalCard
            // Each question is a card of its own, as MessageArea keys it.
            key={approval.pendingId}
            approval={approval}
            state="pending"
            onSubmit={onSubmit}
            onAlwaysAllow={onAlwaysAllow}
          />,
        ),
      );
      return { onSubmit, onAlwaysAllow };
    }

    it('goes with an allowed call when ticked', () => {
      const { onSubmit, onAlwaysAllow } = renderWith({ pendingId: 'p', calls: [call('a')] });
      expect(container.textContent).toContain('以後「加入單字」直接執行');

      click(checkbox()!);
      click(button('允許'));

      expect(onSubmit).toHaveBeenCalledWith([{ toolUseId: 'a', approve: true }]);
      expect(onAlwaysAllow).toHaveBeenCalledWith([expect.objectContaining({ toolUseId: 'a' })]);
    });

    it('is nothing when the call is refused, or the box left alone', () => {
      const first = renderWith({ pendingId: 'p', calls: [call('a')] });
      click(checkbox()!);
      click(button('拒絕'));
      expect(first.onAlwaysAllow).not.toHaveBeenCalled();

      const second = renderWith({ pendingId: 'q', calls: [call('b')] });
      click(button('允許'));
      expect(second.onAlwaysAllow).not.toHaveBeenCalled();
    });

    it('is offered once per tool, and goes once for it when its calls are allowed together', () => {
      const { onAlwaysAllow } = renderWith({
        pendingId: 'p',
        calls: [call('a'), call('b'), call('c', { tool: 'save', displayName: '存到 dashboard' })],
      });
      expect(container.querySelectorAll('input[type="checkbox"]')).toHaveLength(2);

      click(checkbox()!);
      click(button('全部允許（3）'));

      expect(onAlwaysAllow).toHaveBeenCalledWith([expect.objectContaining({ toolUseId: 'a' })]);
    });

    it('goes for a tool whose first call was refused but another allowed', () => {
      const { onAlwaysAllow } = renderWith({ pendingId: 'p', calls: [call('a'), call('b')] });
      click(checkbox()!);
      const toggles = buttons().filter((b) => b.textContent === '允許' || b.textContent === '拒絕');
      click(toggles[1]); // a: 拒絕
      click(toggles[2]); // b: 允許
      click(button('照上面的選擇送出'));

      expect(onAlwaysAllow).toHaveBeenCalledWith([expect.objectContaining({ tool: 'add_word' })]);
    });

    it('is not offered for a destructive call, an outside connector’s, or after outside content', () => {
      renderWith({ pendingId: 'p', calls: [call('a', { effect: 'destructive' })] });
      expect(checkbox()).toBeNull();
      renderWith({ pendingId: 'q', calls: [call('b', { connector: 'inst-1' })] });
      expect(checkbox()).toBeNull();
      renderWith({ pendingId: 'r', calls: [call('c')], tainted: true });
      expect(checkbox()).toBeNull();
    });
  });

  describe('time left to answer', () => {
    afterEach(() => vi.useRealTimers());
    const timeLeft = () =>
      container.querySelector('[data-testid="approval-time-left"]')?.textContent ?? null;

    it('counts down while the card waits, from when the question arrived', () => {
      vi.useFakeTimers();
      const askedAt = Date.now();
      render({ pendingId: 'p', calls: [call('a')], askedAt });
      expect(timeLeft()).toBe('3 分鐘內沒有回覆，這次就不執行');

      act(() => vi.advanceTimersByTime(2 * 60 * 1000 + 30_000));
      expect(timeLeft()).toBe('30 秒內沒有回覆，這次就不執行');
    });

    it('is gone once sent or answered, and not shown without a time', () => {
      vi.useFakeTimers();
      const askedAt = Date.now();
      render({ pendingId: 'p', calls: [call('a')], askedAt }, vi.fn(), 'sending');
      expect(timeLeft()).toBeNull();
      render({ pendingId: 'p', calls: [call('a')], askedAt }, vi.fn(), 'resolved', [
        { toolUseId: 'a', approve: true },
      ]);
      expect(timeLeft()).toBeNull();
      render({ pendingId: 'p', calls: [call('a')] });
      expect(timeLeft()).toBeNull();
    });
  });
});
