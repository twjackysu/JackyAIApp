import { describe, expect, it } from 'vitest';

import studioPromptsCs from '../../../../JackyAIApp.Server/Services/Assistant/Studio/StudioSitePrompts.cs?raw';

import { BRIEFING_PROMPT, clientConfigs, siteMcpUrl } from './clientConfigs';

const url = 'https://apps.jacky-studio.com/api/mcp';
const token = 'jsk_abc';

describe('siteMcpUrl', () => {
  it('is the site’s /api/mcp', () => {
    expect(siteMcpUrl('https://apps.jacky-studio.com')).toBe(url);
    expect(siteMcpUrl('https://localhost:5173/')).toBe('https://localhost:5173/api/mcp');
  });
});

describe('clientConfigs', () => {
  const byKey = Object.fromEntries(clientConfigs(url, token).map((c) => [c.key, c]));

  it('gives Claude Code one command with the header', () => {
    expect(byKey.claudeCode.text).toBe(
      `claude mcp add --transport http jacky-studio ${url} --header "Authorization: Bearer jsk_abc"`,
    );
  });

  it('bridges Claude Desktop through mcp-remote, the token in env', () => {
    const config = JSON.parse(byKey.claudeDesktop.text);
    const server = config.mcpServers['jacky-studio'];
    expect(server.command).toBe('npx');
    expect(server.args).toEqual([
      '-y',
      'mcp-remote',
      url,
      '--header',
      'Authorization:${AUTH_HEADER}',
    ]);
    expect(server.env).toEqual({ AUTH_HEADER: 'Bearer jsk_abc' });
  });

  it('gives Cursor and VS Code the URL and the header', () => {
    expect(JSON.parse(byKey.cursor.text)).toEqual({
      mcpServers: { 'jacky-studio': { url, headers: { Authorization: 'Bearer jsk_abc' } } },
    });
    expect(JSON.parse(byKey.vscode.text)).toEqual({
      servers: {
        'jacky-studio': { type: 'http', url, headers: { Authorization: 'Bearer jsk_abc' } },
      },
    });
  });
});

describe('the ready-made requests (MCP prompts)', () => {
  const byKey = Object.fromEntries(clientConfigs(url, token).map((c) => [c.key, c]));

  it('names the server’s own briefing prompt', () => {
    expect(studioPromptsCs).toContain(`"${BRIEFING_PROMPT}"`);
  });

  it('says each client’s way to them, in its own command shape', () => {
    expect(byKey.claudeCode.prompts).toContain('/mcp__jacky-studio__daily_briefing');
    expect(byKey.vscode.prompts).toContain('/mcp.jacky-studio.daily_briefing');
    expect(byKey.claudeDesktop.prompts).toContain('jacky-studio');
    expect(byKey.cursor.prompts).toBeUndefined();
  });
});
