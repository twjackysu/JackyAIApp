import { describe, expect, it } from 'vitest';

import { callbackNotice } from './callbackNotice';

const of = (query: string) => callbackNotice(new URLSearchParams(query));

describe('callbackNotice', () => {
  it('is nothing for a page not opened by a callback', () => {
    expect(of('')).toBeNull();
    expect(of('success=true')).toBeNull();
    expect(of('provider=Google')).toBeNull();
  });

  it('keeps the account connections’ words', () => {
    expect(of('success=true&provider=Google')).toEqual({
      severity: 'success',
      message: 'Successfully connected to Google',
    });
    expect(of('error=access_denied&provider=Google')?.message).toBe(
      'Access was denied by the user',
    );
    expect(of('error=odd&provider=Google')?.message).toBe('Failed to connect to Google');
  });

  it('tells an AI connector’s reader what happened in their words', () => {
    expect(of('success=true&provider=Notion&kind=mcp')).toEqual({
      severity: 'success',
      message: '已連上「Notion」，AI 現在可以使用它的工具。',
    });
    const denied = of('error=access_denied&provider=Notion&kind=mcp');
    expect(denied?.severity).toBe('error');
    expect(denied?.message).toBe('「Notion」：你在授權頁沒有允許，所以沒有連上。');
    expect(of('error=odd&provider=Linear&kind=mcp')?.message).toBe(
      '「Linear」：沒有連上，請重新授權一次。',
    );
  });

  it('says a sign-in started by another account did not connect', () => {
    // The server leaves out the connector's name: it is not this reader's connector.
    expect(of('error=wrong_account&provider=MCP&kind=mcp')?.message).toContain(
      '不是從你目前登入的帳號開始的',
    );
  });
});
