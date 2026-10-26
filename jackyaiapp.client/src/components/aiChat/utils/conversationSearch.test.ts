import { describe, expect, it } from 'vitest';

import { matchConversations } from './conversationSearch';

const list = [
  { id: '1', title: '台積電近一個月走勢' },
  { id: '2', title: '0050 和 006208 定期定額' },
  { id: '3', title: 'ETF 溢價比較' },
  { id: '4', title: '台積電財報體檢' },
];

describe('matchConversations', () => {
  it('keeps every conversation for an empty query', () => {
    expect(matchConversations(list, '')).toBe(list);
    expect(matchConversations(list, '   ')).toBe(list);
  });

  it('keeps those whose title holds every word, in order', () => {
    expect(matchConversations(list, '台積電').map((c) => c.id)).toEqual(['1', '4']);
    expect(matchConversations(list, '台積電 財報').map((c) => c.id)).toEqual(['4']);
    expect(matchConversations(list, '0050 走勢')).toEqual([]);
  });

  it('ignores case and full-width forms', () => {
    expect(matchConversations(list, 'etf').map((c) => c.id)).toEqual(['3']);
    expect(matchConversations(list, 'ＥＴＦ').map((c) => c.id)).toEqual(['3']);
    expect(matchConversations(list, '００５０').map((c) => c.id)).toEqual(['2']);
  });
});
