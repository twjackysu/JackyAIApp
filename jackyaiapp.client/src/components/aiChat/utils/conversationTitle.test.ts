import { describe, expect, it } from 'vitest';

import chatConversationServiceCs from '../../../../../JackyAIApp.Server/Services/Chatbot/ChatConversationService.cs?raw';

import { MAX_TITLE_LENGTH, renamedTitle } from './conversationTitle';

describe('renamedTitle (D-144)', () => {
  it('keeps what was typed on one line, its spaces run together', () => {
    expect(renamedTitle('  台積電\n財報\t 重點  ', '舊標題')).toBe('台積電 財報 重點');
    expect(renamedTitle('ETF　比較', '舊標題')).toBe('ETF 比較');
  });

  it('saves nothing for an empty title or the one it already has', () => {
    expect(renamedTitle('   ', '舊標題')).toBeNull();
    expect(renamedTitle('', '舊標題')).toBeNull();
    expect(renamedTitle(' 舊標題 ', '舊標題')).toBeNull();
  });

  it('saves nothing longer than the server takes, counting an emoji as one', () => {
    expect(renamedTitle('字'.repeat(MAX_TITLE_LENGTH), '舊')).toBe('字'.repeat(MAX_TITLE_LENGTH));
    expect(renamedTitle('字'.repeat(MAX_TITLE_LENGTH + 1), '舊')).toBeNull();
    expect(renamedTitle('📈'.repeat(MAX_TITLE_LENGTH), '舊')).toBe('📈'.repeat(MAX_TITLE_LENGTH));
  });

  it('takes as long a title as the server does', () => {
    const max = /const int MaxTitleLength = (\d+);/.exec(chatConversationServiceCs);
    expect(Number(max?.[1])).toBe(MAX_TITLE_LENGTH);
  });
});
