/**
 * What the reader is told when a chat turn fails. The server sends only a code (ChatErrorCodes on
 * the backend) — the wording lives here, and what actually went wrong is in the server log.
 */
export const CHAT_ERROR_MESSAGES = {
  context_too_long: '這段對話太長了，請開新對話繼續。',
  request_too_large: '這次送出的內容太大，請縮短或分次送出。',
  busy: 'AI 服務目前忙碌中，請稍後再試。',
  unavailable: 'AI 助手暫時無法使用，請聯絡站長。',
  refused: '這個問題我無法回答，請換個問法，或詢問台股相關的問題。',
  no_answer: '沒有收到完整回覆，可能是這次查詢需要的資料較多，請再試一次或縮短問題範圍。',
  failed: '回覆失敗，請稍後再試。',
} as const;

export type ChatErrorCode = keyof typeof CHAT_ERROR_MESSAGES;

/** The message for a code from the server; an unknown or missing code reads as a plain failure. */
export const chatErrorMessage = (code: string | undefined): string =>
  code && code in CHAT_ERROR_MESSAGES
    ? CHAT_ERROR_MESSAGES[code as ChatErrorCode]
    : CHAT_ERROR_MESSAGES.failed;

/** Shown under an answer that ran into the length limit. */
export const TRUNCATED_NOTE = '回覆因長度上限被截斷，內容可能不完整。';
