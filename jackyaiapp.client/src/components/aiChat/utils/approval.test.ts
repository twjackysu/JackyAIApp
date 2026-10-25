import { describe, expect, it } from 'vitest';

import clientToolRunnerCs from '../../../../../JackyAIApp.Server/Services/Chatbot/ClientToolRunner.cs?raw';

import {
  alwaysAllowRequest,
  APPROVAL_TIME_MARGIN_MS,
  APPROVAL_TIMEOUT_MS,
  approvalTimeLeft,
  canAlwaysAllow,
  decisionsFor,
  formatArgument,
  hasOpenApproval,
  resolveApproval,
  type ApprovalCall,
  type MessageApproval,
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
    expect(canAlwaysAllow(siteWrite({ alwaysAsks: true }))).toBe(false);
    expect(canAlwaysAllow(siteWrite({ connector: 'inst-1', tool: 'm1__create' }))).toBe(false);
    expect(canAlwaysAllow(siteWrite({}), true)).toBe(false);
  });
});

describe('hasOpenApproval', () => {
  const approval = (state: MessageApproval['state']): { approval: MessageApproval } => ({
    approval: { request: { pendingId: 'p1', calls: [call('t1')] }, state },
  });

  it('is open while a card waits, is being sent, or failed to send', () => {
    expect(hasOpenApproval([{}, approval('pending')])).toBe(true);
    expect(hasOpenApproval([approval('sending')])).toBe(true);
    expect(hasOpenApproval([approval('failed')])).toBe(true);
  });

  it('is closed with no card or only answered ones', () => {
    expect(hasOpenApproval([])).toBe(false);
    expect(hasOpenApproval([{}, approval('resolved')])).toBe(false);
  });
});

describe('approvalTimeLeft', () => {
  const asked = 1_000_000;
  // A little before the server's own limit, which started a moment before the question arrived.
  const deadline = asked + APPROVAL_TIMEOUT_MS - APPROVAL_TIME_MARGIN_MS;

  it("counts to the server's own limit", () => {
    const minutes = /ApprovalTimeout = TimeSpan\.FromMinutes\((\d+)\)/.exec(clientToolRunnerCs);
    expect(minutes).not.toBeNull();
    expect(Number(minutes![1]) * 60 * 1000).toBe(APPROVAL_TIMEOUT_MS);
  });

  it('says nothing without knowing when it was asked', () => {
    expect(approvalTimeLeft(undefined, asked)).toBeNull();
  });

  it('counts whole minutes, then seconds in the last minute, then that the time is up', () => {
    expect(approvalTimeLeft(asked, asked)).toEqual({
      text: '3 分鐘內沒有回覆，這次就不執行',
      urgent: false,
    });
    expect(approvalTimeLeft(asked, asked + 61_000)).toEqual({
      text: '2 分鐘內沒有回覆，這次就不執行',
      urgent: false,
    });
    expect(approvalTimeLeft(asked, deadline - 60_000)).toEqual({
      text: '60 秒內沒有回覆，這次就不執行',
      urgent: true,
    });
    expect(approvalTimeLeft(asked, deadline - 4_500)).toEqual({
      text: '5 秒內沒有回覆，這次就不執行',
      urgent: true,
    });
    expect(approvalTimeLeft(asked, deadline)).toEqual({
      text: '時間到了，這次不會執行',
      urgent: true,
    });
  });
});

describe('resolveApproval', () => {
  it('takes all-no after the reader sent an allow, or after a failed send, as the time running out', () => {
    const allow = [
      { toolUseId: 't1', approve: true },
      { toolUseId: 't2', approve: false },
    ];
    const card = { request: { pendingId: 'p1', calls: [call('t1'), call('t2')] } };
    const allNo = allow.map((d) => ({ ...d, approve: false }));
    expect(resolveApproval({ ...card, state: 'sending', decisions: allow }, allNo).expired).toBe(
      true,
    );
    expect(resolveApproval({ ...card, state: 'failed', decisions: allow }, allNo).expired).toBe(
      true,
    );
  });

  const waiting = (state: MessageApproval['state']): MessageApproval => ({
    request: { pendingId: 'p1', calls: [call('t1'), call('t2')] },
    state,
  });
  const no = [
    { toolUseId: 't1', approve: false },
    { toolUseId: 't2', approve: false },
  ];

  it('takes all-no for a card the reader never sent as the time running out', () => {
    expect(resolveApproval(waiting('pending'), no)).toEqual({
      ...waiting('pending'),
      state: 'resolved',
      decisions: no,
      expired: true,
    });
  });

  it("keeps the reader's own no as theirs, and anything allowed as an answer", () => {
    expect(resolveApproval({ ...waiting('sending'), decisions: no }, no).expired).toBe(false);
    expect(resolveApproval({ ...waiting('failed'), decisions: no }, no).expired).toBe(false);
    expect(
      resolveApproval(waiting('pending'), [
        { toolUseId: 't1', approve: true },
        { toolUseId: 't2', approve: false },
      ]).expired,
    ).toBe(false);
  });
});
