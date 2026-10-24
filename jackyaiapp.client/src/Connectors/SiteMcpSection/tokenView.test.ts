import { describe, expect, it } from 'vitest';

import { tokenView } from './tokenView';

import type { SiteAccessToken } from '@/apis/connectorsApis/types';

const now = Date.parse('2026-10-24T10:00:00Z');
const token = (over: Partial<SiteAccessToken>): SiteAccessToken => ({
  id: 't1',
  name: 'Claude Desktop',
  hint: 'jsk_AbC…xYz9',
  allowWrites: false,
  viaOAuth: false,
  createdAt: '2026-10-24T09:00:00',
  expiresAt: '2027-01-22T09:00:00',
  lastUsedAt: null,
  ...over,
});

describe('tokenView', () => {
  it('says when a token ends, and that it was never used', () => {
    const v = tokenView(token({}), now);
    expect(v.expired).toBe(false);
    expect(v.expires).toMatch(/^2027\/01\/2[12] 到期$/);
    expect(v.lastUsed).toBe('從未使用');
  });

  it('counts the last week in days', () => {
    expect(tokenView(token({ expiresAt: '2026-10-27T09:00:00' }), now).expires).toBe('3 天後到期');
    expect(tokenView(token({ expiresAt: '2026-10-24T11:00:00' }), now).expires).toBe('1 天後到期');
  });

  it('reads the server’s zoneless times as UTC', () => {
    expect(tokenView(token({ expiresAt: '2026-10-24T09:59:00' }), now).expired).toBe(true);
    expect(tokenView(token({ expiresAt: '2026-10-24T10:01:00Z' }), now).expired).toBe(false);
  });

  it('says a token ran out', () => {
    expect(tokenView(token({ expiresAt: '2026-10-01T00:00:00' }), now)).toMatchObject({
      expired: true,
      expires: '已過期',
    });
  });

  it('says when it was last used', () => {
    expect(tokenView(token({ lastUsedAt: '2026-10-20T03:00:00' }), now).lastUsed).toMatch(
      /^最後使用 2026\/10\/20$/,
    );
  });
});
