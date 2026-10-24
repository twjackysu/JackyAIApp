import type { CreateInstallationRequest, McpProbeResult } from '@/apis/connectorsApis/types';

/** The dialog's three steps (docs/ai-native/design/05 §3): where the server is, how to sign in, done. */
export type AddMcpStep = 'locate' | 'credentials' | 'done';

export interface CredentialsForm {
  /** For an API key: the header it goes in. A bearer token goes in Authorization. */
  kind: 'bearer' | 'apiKey';
  headerName: string;
  value: string;
  clientId: string;
  clientSecret: string;
}

export const EMPTY_CREDENTIALS: CredentialsForm = {
  kind: 'bearer',
  headerName: 'X-API-Key',
  value: '',
  clientId: '',
  clientSecret: '',
};

/**
 * Why the URL cannot be checked yet, or null. The server checks it again, and is the one that
 * refuses private and reserved addresses (docs/ai-native/design/06); this only catches typos early.
 */
export function urlProblem(raw: string): string | null {
  const url = raw.trim();
  if (!url) return '請輸入 MCP server 的網址';
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return '這不是一個完整的網址（例如 https://mcp.example.com/mcp）';
  }
  if (parsed.protocol !== 'https:') return 'MCP server 必須使用 https';
  if (parsed.username || parsed.password) return '網址不能含帳號密碼，請在下一步填認證資料';
  return null;
}

/** The name the dialog suggests: the server's own, else its host. */
export function suggestedName(probe: McpProbeResult): string {
  if (probe.serverName?.trim()) return probe.serverName.trim();
  try {
    return new URL(probe.serverUrl).hostname;
  } catch {
    return probe.serverUrl;
  }
}

/** What the reader still has to fill in before the server can be added, or null. */
export function credentialsProblem(
  probe: McpProbeResult,
  name: string,
  form: CredentialsForm,
): string | null {
  if (!name.trim()) return '請幫這個 connector 取個名字';
  if (name.trim().length > 100) return '名字最多 100 個字';
  if (probe.auth === 'apiKey') {
    if (form.kind === 'apiKey' && !/^[A-Za-z0-9-]+$/.test(form.headerName.trim()))
      return 'Header 名稱只能有英數字和 -';
    if (!form.value.trim()) return form.kind === 'bearer' ? '請填入 token' : '請填入 API key';
  }
  if (probe.auth === 'oauth' && probe.registration === 'manual' && !form.clientId.trim())
    return '這個授權伺服器不接受自動註冊，請填入 client ID';
  return null;
}

/** The request that adds the server, carrying only the credentials its kind of sign-in uses. */
export function createRequest(
  probe: McpProbeResult,
  name: string,
  form: CredentialsForm,
): CreateInstallationRequest {
  const base = { definitionKey: 'custom', displayName: name.trim(), serverUrl: probe.serverUrl };
  switch (probe.auth) {
    case 'none':
      return { ...base, authKind: 'none' };
    case 'apiKey':
      return form.kind === 'bearer'
        ? {
            ...base,
            authKind: 'bearer',
            credentials: { headerName: 'Authorization', value: form.value.trim() },
          }
        : {
            ...base,
            authKind: 'apiKey',
            credentials: { headerName: form.headerName.trim(), value: form.value.trim() },
          };
    case 'oauth':
      return probe.registration === 'manual'
        ? {
            ...base,
            authKind: 'oauth',
            credentials: {
              clientId: form.clientId.trim(),
              clientSecret: form.clientSecret.trim() || undefined,
            },
          }
        : { ...base, authKind: 'oauth' };
  }
}

/** One line on what was found at the URL, for the reader to confirm before going on. */
export function probeSummary(probe: McpProbeResult): string {
  switch (probe.auth) {
    case 'none':
      return '找到了！這個 server 不需要登入，可以直接連線。';
    case 'apiKey':
      return '找到了！這個 server 需要 API key 或 token。';
    case 'oauth': {
      const by = probe.authorizationServer ? `（${probe.authorizationServer}）` : '';
      return `找到了！這個 server 用 OAuth 登入${by}，下一步會帶你去授權。`;
    }
  }
}
