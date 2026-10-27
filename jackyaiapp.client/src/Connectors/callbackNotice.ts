/** What the page says when an OAuth sign-in sends the reader back (`?success=true` or `?error=…`, with `provider`). */
export interface CallbackNotice {
  message: string;
  severity: 'success' | 'error';
}

// The account connections' words, as they always were.
const ACCOUNT_ERRORS: Record<string, string> = {
  access_denied: 'Access was denied by the user',
  invalid_request: 'Invalid request parameters',
  callback_failed: 'Failed to process the authorization',
  internal_error: 'An internal error occurred',
  missing_parameters: 'Missing required parameters',
  invalid_provider: 'Invalid provider specified',
};

// An AI connector's sign-in (`kind=mcp`): what happened, and what to do about it.
const MCP_ERRORS: Record<string, string> = {
  access_denied: '你在授權頁沒有允許，所以沒有連上。',
  invalid_request:
    '這次授權的回應無效或已過期（要在 10 分鐘內完成），請到 connector 重新授權一次。',
  callback_failed: '授權伺服器沒有發出 token，請稍後重新授權；connector 的詳細資料會顯示原因。',
  missing_parameters: '授權回應缺少必要的資料，請到 connector 重新授權一次。',
  // The sign-in was started by another account than the one signed in here (or by a link someone sent).
  wrong_account:
    '這次授權不是從你目前登入的帳號開始的，所以沒有連上。如果是你自己開始的，請用同一個帳號登入後到 connector 重新授權。',
};

/**
 * The notice for the callback's query, or null when the page was not opened by one.
 *
 * Anyone can send a reader a link to /connectors with a query of their own, so the name the query carries is
 * shown only when it is one of the reader's own (`knownNames`: their account connections and AI connectors);
 * otherwise the notice speaks of the account or the connector without it, never in someone else's words (D-162).
 */
export function callbackNotice(
  params: URLSearchParams,
  knownNames: readonly string[] = [],
): CallbackNotice | null {
  const named = params.get('provider');
  if (!named) return null;
  const mcp = params.get('kind') === 'mcp';
  const known = knownNames.some((name) => name.toLowerCase() === named.toLowerCase());
  if (!known) return unnamedNotice(params, mcp);
  const provider = named;
  if (params.get('success') === 'true') {
    return {
      severity: 'success',
      message: mcp
        ? `已連上「${provider}」，AI 現在可以使用它的工具。`
        : `Successfully connected to ${provider}`,
    };
  }
  const error = params.get('error');
  if (!error) return null;
  const message = mcp
    ? (MCP_ERRORS[error] ?? '沒有連上，請重新授權一次。')
    : (ACCOUNT_ERRORS[error] ?? `Failed to connect to ${provider}`);
  return { severity: 'error', message: mcp ? `「${provider}」：${message}` : message };
}

/** The same notice without a name: the query named something the reader does not have. */
function unnamedNotice(params: URLSearchParams, mcp: boolean): CallbackNotice | null {
  if (params.get('success') === 'true') {
    return {
      severity: 'success',
      message: mcp ? '已連上 connector，AI 現在可以使用它的工具。' : 'Successfully connected',
    };
  }
  const error = params.get('error');
  if (!error) return null;
  const message = mcp
    ? (MCP_ERRORS[error] ?? '沒有連上，請重新授權一次。')
    : (ACCOUNT_ERRORS[error] ?? 'Failed to connect');
  return { severity: 'error', message };
}
