import { describe, expect, it } from 'vitest';

import { probe } from '../testData';

import {
  createRequest,
  credentialsProblem,
  EMPTY_CREDENTIALS,
  probeSummary,
  suggestedName,
  urlProblem,
} from './addMcpServer';

describe('urlProblem', () => {
  it('accepts an https URL', () => {
    expect(urlProblem(' https://mcp.example.com/mcp ')).toBeNull();
  });
  it('asks for one when empty', () => {
    expect(urlProblem('  ')).toBe('請輸入 MCP server 的網址');
  });
  it('refuses plain http', () => {
    expect(urlProblem('http://mcp.example.com')).toBe('MCP server 必須使用 https');
  });
  it('refuses something that is not a URL', () => {
    expect(urlProblem('mcp.example.com')).toContain('不是一個完整的網址');
  });
  it('refuses credentials in the URL, which would be stored in the clear', () => {
    expect(urlProblem('https://me:secret@mcp.example.com')).toContain('不能含帳號密碼');
  });
});

describe('suggestedName', () => {
  it("uses the server's own name", () => {
    expect(suggestedName(probe({ serverName: ' Linear ' }))).toBe('Linear');
  });
  it('falls back to the host', () => {
    expect(suggestedName(probe())).toBe('mcp.example.com');
  });
});

describe('credentialsProblem', () => {
  const form = { ...EMPTY_CREDENTIALS };

  it('needs a name', () => {
    expect(credentialsProblem(probe(), ' ', form)).toBe('請幫這個 connector 取個名字');
  });
  it('needs nothing else for a server without sign-in', () => {
    expect(credentialsProblem(probe(), 'x', form)).toBeNull();
  });
  it('needs the token for a bearer server', () => {
    expect(credentialsProblem(probe({ auth: 'apiKey' }), 'x', form)).toBe('請填入 token');
  });
  it('needs a valid header name for an API key', () => {
    const f = { ...form, kind: 'apiKey' as const, headerName: 'X Key', value: 'k' };
    expect(credentialsProblem(probe({ auth: 'apiKey' }), 'x', f)).toContain('Header');
  });
  it('needs a client ID only when the server cannot register us itself', () => {
    expect(
      credentialsProblem(probe({ auth: 'oauth', registration: 'cimd' }), 'x', form),
    ).toBeNull();
    expect(
      credentialsProblem(probe({ auth: 'oauth', registration: 'manual' }), 'x', form),
    ).toContain('client ID');
  });
});

describe('createRequest', () => {
  const form = {
    ...EMPTY_CREDENTIALS,
    value: ' tok ',
    headerName: 'X-Key',
    clientId: 'cid',
    clientSecret: '',
  };

  it('carries no credentials for a server without sign-in', () => {
    expect(createRequest(probe(), ' Mine ', form)).toEqual({
      definitionKey: 'custom',
      displayName: 'Mine',
      serverUrl: 'https://mcp.example.com/mcp',
      authKind: 'none',
    });
  });
  it('puts a bearer token in Authorization', () => {
    expect(createRequest(probe({ auth: 'apiKey' }), 'x', form).credentials).toEqual({
      headerName: 'Authorization',
      value: 'tok',
    });
  });
  it('puts an API key in its own header', () => {
    const r = createRequest(probe({ auth: 'apiKey' }), 'x', { ...form, kind: 'apiKey' });
    expect(r.authKind).toBe('apiKey');
    expect(r.credentials).toEqual({ headerName: 'X-Key', value: 'tok' });
  });
  it('sends a client only when the reader had to give one', () => {
    expect(
      createRequest(probe({ auth: 'oauth', registration: 'dcr' }), 'x', form).credentials,
    ).toBe(undefined);
    expect(
      createRequest(probe({ auth: 'oauth', registration: 'manual' }), 'x', form).credentials,
    ).toEqual({ clientId: 'cid', clientSecret: undefined });
  });
});

describe('probeSummary', () => {
  it('names the authorization server for OAuth', () => {
    expect(
      probeSummary(probe({ auth: 'oauth', authorizationServer: 'auth.linear.app' })),
    ).toContain('auth.linear.app');
  });
});
