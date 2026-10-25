/** The site's own MCP server, at this site's address (the same for every reader). */
export const siteMcpUrl = (origin: string) => `${origin.replace(/\/+$/, '')}/api/mcp`;

/** The name the other AI lists the server under. */
export const SERVER_KEY = 'jacky-studio';

/**
 * The morning briefing among the server's ready-made requests (MCP prompts: the server's `SitePrompt`s,
 * `StudioSitePrompts.DailyBriefing`; its test holds the two together), the one the dialog names.
 */
export const BRIEFING_PROMPT = 'daily_briefing';

export interface ClientConfig {
  key: 'claudeCode' | 'claudeDesktop' | 'cursor' | 'vscode';
  label: string;
  /** Where it goes, in words. */
  where: string;
  text: string;
  /**
   * Where that AI shows the site's ready-made requests (MCP prompts), in words, naming the briefing.
   * None for a client whose way to them we do not know.
   */
  prompts?: string;
}

const json = (value: unknown) => JSON.stringify(value, null, 2);

/**
 * How to add the server to the AIs readers most often have, with the token filled in. Each takes a
 * Streamable HTTP server with a header of ours, except Claude Desktop's config file, which only starts
 * local programs: `mcp-remote` bridges it (the token goes through `env`, since an argument with a
 * space breaks on Windows).
 */
export function clientConfigs(serverUrl: string, token: string): ClientConfig[] {
  const authorization = `Bearer ${token}`;
  return [
    {
      key: 'claudeCode',
      label: 'Claude Code',
      where: '在終端機執行',
      text: `claude mcp add --transport http ${SERVER_KEY} ${serverUrl} --header "Authorization: ${authorization}"`,
      prompts: `輸入 /mcp__${SERVER_KEY}__${BRIEFING_PROMPT} 就是今日簡報；打 /mcp__${SERVER_KEY} 看全部`,
    },
    {
      key: 'claudeDesktop',
      label: 'Claude Desktop',
      where: '設定 → 開發者 → 編輯設定（claude_desktop_config.json），需要 Node.js',
      text: json({
        mcpServers: {
          [SERVER_KEY]: {
            command: 'npx',
            args: ['-y', 'mcp-remote', serverUrl, '--header', 'Authorization:${AUTH_HEADER}'],
            env: { AUTH_HEADER: authorization },
          },
        },
      }),
      prompts: `輸入框的「＋」→ ${SERVER_KEY}，選「今日簡報」或其他現成指令`,
    },
    {
      key: 'cursor',
      label: 'Cursor',
      where: '~/.cursor/mcp.json',
      text: json({
        mcpServers: { [SERVER_KEY]: { url: serverUrl, headers: { Authorization: authorization } } },
      }),
    },
    {
      key: 'vscode',
      label: 'VS Code',
      where: '.vscode/mcp.json',
      text: json({
        servers: {
          [SERVER_KEY]: { type: 'http', url: serverUrl, headers: { Authorization: authorization } },
        },
      }),
      prompts: `在 Copilot Chat 輸入 /mcp.${SERVER_KEY}.${BRIEFING_PROMPT} 就是今日簡報`,
    },
  ];
}
