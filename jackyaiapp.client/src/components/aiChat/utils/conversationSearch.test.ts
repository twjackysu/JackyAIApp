import { describe, expect, it } from 'vitest';

import chatConversationServiceCs from '../../../../../JackyAIApp.Server/Services/Chatbot/ChatConversationService.cs?raw';

import {
  findConversations,
  highlightParts,
  matchConversations,
  MAX_SEARCH_LENGTH,
} from './conversationSearch';

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

describe('findConversations', () => {
  const hits = [
    { conversationId: '3', snippet: '…台積電的溢價…' },
    { conversationId: '1', snippet: '不會用到' },
    { conversationId: 'gone', snippet: '已刪除' },
  ];

  it('adds the conversations found in their messages, in the list order, with their snippet', () => {
    expect(findConversations(list, '台積電', hits)).toEqual([
      { conversation: list[0] },
      { conversation: list[2], snippet: '…台積電的溢價…' },
      { conversation: list[3] },
    ]);
  });

  it('is the title search alone while the messages have not answered', () => {
    expect(findConversations(list, '台積電', undefined).map((f) => f.conversation.id)).toEqual([
      '1',
      '4',
    ]);
  });

  it('keeps everything, without snippets, for an empty query', () => {
    expect(findConversations(list, ' ', []).map((f) => f.snippet)).toEqual([
      undefined,
      undefined,
      undefined,
      undefined,
    ]);
  });
});

describe('highlightParts', () => {
  it('marks every place of every word, any case', () => {
    expect(highlightParts('ETF 跟 etf 和台積電', 'etf 台積電')).toEqual([
      { text: 'ETF', match: true },
      { text: ' 跟 ', match: false },
      { text: 'etf', match: true },
      { text: ' 和', match: false },
      { text: '台積電', match: true },
    ]);
  });

  it('matches full-width forms, and marks nothing for an empty query', () => {
    expect(highlightParts('ＥＴＦ溢價', 'etf')[0]).toEqual({ text: 'ＥＴＦ', match: true });
    expect(highlightParts('文字', ' ')).toEqual([{ text: '文字', match: false }]);
  });

  it('keeps its places where a character folds to more than one (…)', () => {
    expect(highlightParts('…毛利率 59%…', '毛利率')).toEqual([
      { text: '…', match: false },
      { text: '毛利率', match: true },
      { text: ' 59%…', match: false },
    ]);
  });
});

describe('MAX_SEARCH_LENGTH', () => {
  it('is what the server takes', () => {
    const max = /const int MaxSearchLength = (\d+);/.exec(chatConversationServiceCs);
    expect(Number(max?.[1])).toBe(MAX_SEARCH_LENGTH);
  });
});
