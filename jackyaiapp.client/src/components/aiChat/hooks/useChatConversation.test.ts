import { describe, expect, it } from 'vitest';

import { mapHistoryToMessages } from './useChatConversation';

describe('mapHistoryToMessages', () => {
  it('turns stored turns into bubbles: the server id kept for editing, the files kept', () => {
    const file = {
      id: 3,
      fileName: '持股.xlsx',
      kind: 'excel' as const,
      charCount: 10,
      truncated: false,
    };
    expect(
      mapHistoryToMessages([
        {
          id: 1,
          role: 'user',
          content: '看這份',
          createdAt: '2026-10-19T09:00:00Z',
          attachments: [file],
        },
        { id: 2, role: 'assistant', content: '好的', createdAt: '2026-10-19T09:00:05Z' },
      ]),
    ).toEqual([
      {
        id: '1',
        serverId: 1,
        content: '看這份',
        isUser: true,
        timestamp: new Date('2026-10-19T09:00:00Z'),
        attachments: [file],
      },
      {
        id: '2',
        serverId: 2,
        content: '好的',
        isUser: false,
        timestamp: new Date('2026-10-19T09:00:05Z'),
        attachments: undefined,
      },
    ]);
  });

  it("brings back an answer's stored cards, checked as a streamed one is", () => {
    const [message] = mapHistoryToMessages([
      {
        id: 2,
        role: 'assistant',
        content: '如下表',
        createdAt: '2026-10-19T09:00:05Z',
        cards: [
          { kind: 'link', title: '單字', href: '/english/dictionary/serendipity' },
          { kind: 'link', title: '外站', href: 'javascript:alert(1)' },
          'not a card',
        ],
      },
    ]);
    expect(message.cards).toEqual([
      expect.objectContaining({ kind: 'link', href: '/english/dictionary/serendipity' }),
    ]);
  });

  it('leaves out cards when an answer has none', () => {
    const [message] = mapHistoryToMessages([
      { id: 2, role: 'assistant', content: '好', createdAt: '2026-10-19T09:00:05Z', cards: null },
    ]);
    expect(message).not.toHaveProperty('cards');
  });
});
