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
});
