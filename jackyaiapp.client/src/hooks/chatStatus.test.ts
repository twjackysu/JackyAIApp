import { describe, expect, it } from 'vitest';

import { ANSWERING, WAITING_FOR_READER, statusAfter } from './chatStatus';

describe('statusAfter', () => {
  it('names a tool by the name the reader is shown', () => {
    expect(
      statusAfter({ type: 'tool_use', id: 't1', name: 'look_up_word', displayName: '查字典' })
        .statusText,
    ).toBe('🔄 正在使用「查字典」…');
  });

  it('names a hosted tool, which has no display name, as before', () => {
    expect(statusAfter({ type: 'tool_use', name: 'get_stock_history' }).statusText).toBe(
      '🔄 正在查詢 get_stock_history...',
    );
  });

  it('says the turn waits on the reader while a write is put to them', () => {
    expect(statusAfter({ type: 'approval_required', pendingId: 'p1', calls: [] }).statusText).toBe(
      WAITING_FOR_READER,
    );
  });

  it('goes back to answering once a result or the decisions are in', () => {
    expect(statusAfter({ type: 'tool_result', id: 't1', name: 'x', ok: true }).statusText).toBe(
      ANSWERING,
    );
    expect(
      statusAfter({ type: 'approval_resolved', pendingId: 'p1', decisions: [] }).statusText,
    ).toBe(ANSWERING);
  });
});
