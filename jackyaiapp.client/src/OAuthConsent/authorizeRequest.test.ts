import { describe, expect, it } from 'vitest';

import { authorizeRequestOf } from './authorizeRequest';

describe('authorizeRequestOf', () => {
  it('keeps the OAuth fields as they came, and nothing else', () => {
    expect(
      authorizeRequestOf(
        '?response_type=code&client_id=abc&redirect_uri=https%3A%2F%2Fclaude.ai%2Fcb&state=s%201&scope=mcp%3Aread&other=x',
      ),
    ).toEqual({
      response_type: 'code',
      client_id: 'abc',
      redirect_uri: 'https://claude.ai/cb',
      state: 's 1',
      scope: 'mcp:read',
    });
  });

  it('is empty for no query', () => {
    expect(authorizeRequestOf('')).toEqual({});
  });
});
