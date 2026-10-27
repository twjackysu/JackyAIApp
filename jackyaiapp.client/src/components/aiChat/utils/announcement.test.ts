import { describe, expect, it } from 'vitest';

import {
  ANSWERED_ANNOUNCEMENT,
  approvalAnnouncement,
  finishedAnnouncement,
  pendingApprovalOf,
  STOPPED_ANNOUNCEMENT,
} from './announcement';

import type { Message } from '../types';
import type { MessageApproval } from './approval';

const bot = (over: Partial<Message>): Message => ({
  id: 'b',
  content: '',
  isUser: false,
  timestamp: new Date(0),
  ...over,
});

const card = (state: MessageApproval['state'], pendingId = 'p1'): MessageApproval => ({
  state,
  request: {
    pendingId,
    calls: [
      {
        toolUseId: 't1',
        connector: 'site',
        tool: 'update_holding',
        displayName: '更新持股',
        effect: 'write',
        input: {},
      },
      {
        toolUseId: 't2',
        connector: 'site',
        tool: 'update_holding',
        displayName: '更新持股',
        effect: 'write',
        input: {},
      },
      {
        toolUseId: 't3',
        connector: 'c1',
        tool: 'jira_add_comment',
        displayName: '',
        effect: 'write',
        input: {},
      },
    ],
  },
});

describe('finishedAnnouncement', () => {
  it('says an answer is done, without reading it', () => {
    expect(finishedAnnouncement(bot({ answered: true, content: '一大段回答' }))).toBe(
      ANSWERED_ANNOUNCEMENT,
    );
  });

  it('says a stopped answer stopped, not its partial text', () => {
    expect(finishedAnnouncement(bot({ content: '台積電目前\n\n（已中斷）' }))).toBe(
      STOPPED_ANNOUNCEMENT,
    );
  });

  it('says why a failed one failed, cut short', () => {
    expect(finishedAnnouncement(bot({ content: '服務忙碌，請稍後再試。' }))).toBe(
      '回覆沒有完成：服務忙碌，請稍後再試。',
    );
    expect(finishedAnnouncement(bot({ content: '錯'.repeat(400) })).length).toBeLessThan(170);
    expect(finishedAnnouncement(bot({ content: '' }))).toBe('回覆沒有完成。');
  });

  it('says nothing when the last message is not an answer', () => {
    expect(finishedAnnouncement(undefined)).toBe('');
    expect(finishedAnnouncement(bot({ isUser: true, content: '問題' }))).toBe('');
  });
});

describe('approvalAnnouncement', () => {
  it('names each tool waiting once, by its card name', () => {
    expect(approvalAnnouncement(card('pending'))).toBe(
      '需要你的確認：更新持股、jira_add_comment。確認卡在回答下方。',
    );
  });
});

describe('pendingApprovalOf', () => {
  it('finds only a card still waiting for the reader', () => {
    expect(
      pendingApprovalOf([
        bot({ approval: card('resolved') }),
        bot({ approval: card('pending', 'p2') }),
      ])?.request.pendingId,
    ).toBe('p2');
    expect(pendingApprovalOf([bot({ approval: card('sending') })])).toBeUndefined();
  });
});
