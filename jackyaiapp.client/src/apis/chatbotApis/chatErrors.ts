import { ErrorCodes } from '../types';

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

/** The file types the server takes (AttachmentTextExtractor.KindOf), for the file picker. */
export const ATTACHMENT_ACCEPT = '.docx,.xlsx,.csv,.txt,.md,.json,.pdf,.png,.jpg,.jpeg,.gif,.webp';

/** Files sent with one message (ChatAttachmentLimits.MaxPerMessage). */
export const MAX_ATTACHMENTS = 5;

/** Largest upload, matching the server's limit. */
export const MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;

/** Pages per PDF (AttachmentFileProcessor.MaxPdfPages). */
export const MAX_PDF_PAGES = 100;

export const ATTACHMENT_ERROR_MESSAGES = {
  unsupported: '不支援這種檔案，請上傳 Word、Excel、CSV、文字檔、PDF 或圖片。',
  tooLarge: '檔案太大，上限 10 MB。',
  unreadable: '讀不出這個檔案的內容，可能已損毀、有密碼保護，或沒有文字。',
  tooManyPages: `PDF 頁數太多，上限 ${MAX_PDF_PAGES} 頁，請只留需要的部分。`,
  tooMany: `一次最多附加 ${MAX_ATTACHMENTS} 個檔案。`,
  failed: '上傳失敗，請稍後再試。',
} as const;

/** Shown on an attachment whose text was cut to the server's limit. */
export const ATTACHMENT_TRUNCATED_NOTE = '內容過長，只讀取了前段';

/**
 * The message for a failed upload, judged on the error code (or a bare 413 from the server's
 * body-size limit, which has no envelope); the server's own wording is never shown.
 */
export const attachmentErrorMessage = (error: unknown): string => {
  const e = error as { status?: unknown; data?: { error?: { code?: unknown } } } | undefined;
  switch (e?.data?.error?.code) {
    case ErrorCodes.AttachmentUnsupportedType:
      return ATTACHMENT_ERROR_MESSAGES.unsupported;
    case ErrorCodes.PayloadTooLarge:
      return ATTACHMENT_ERROR_MESSAGES.tooLarge;
    case ErrorCodes.AttachmentUnreadable:
      return ATTACHMENT_ERROR_MESSAGES.unreadable;
    case ErrorCodes.AttachmentTooManyPages:
      return ATTACHMENT_ERROR_MESSAGES.tooManyPages;
  }
  return e?.status === 413 ? ATTACHMENT_ERROR_MESSAGES.tooLarge : ATTACHMENT_ERROR_MESSAGES.failed;
};
