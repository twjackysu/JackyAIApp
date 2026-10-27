import { describe, expect, it } from 'vitest';

import chatbotControllerCs from '../../../../JackyAIApp.Server/Controllers/ChatbotController.cs?raw';

import {
  ATTACHMENT_ERROR_MESSAGES,
  attachmentErrorMessage,
  isRetryableChatError,
  MAX_MESSAGE_LENGTH,
  messageTooLongNote,
} from './chatErrors';

const envelope = (status: number, code: string) => ({
  status,
  data: { error: { code, message: 'server wording' } },
});

describe('attachmentErrorMessage', () => {
  it('reads the error code, never the server wording', () => {
    expect(attachmentErrorMessage(envelope(415, 'AttachmentUnsupportedType'))).toBe(
      ATTACHMENT_ERROR_MESSAGES.unsupported,
    );
    expect(attachmentErrorMessage(envelope(413, 'PayloadTooLarge'))).toBe(
      ATTACHMENT_ERROR_MESSAGES.tooLarge,
    );
    expect(attachmentErrorMessage(envelope(400, 'AttachmentUnreadable'))).toBe(
      ATTACHMENT_ERROR_MESSAGES.unreadable,
    );
  });

  it('an image larger than this page makes one asks for a reload', () => {
    expect(attachmentErrorMessage(envelope(400, 'AttachmentImageTooLarge'))).toBe(
      ATTACHMENT_ERROR_MESSAGES.staleImage,
    );
  });

  it('a bare 413 from the body-size limit is a file that is too large', () => {
    expect(attachmentErrorMessage({ status: 413, data: '' })).toBe(
      ATTACHMENT_ERROR_MESSAGES.tooLarge,
    );
  });

  it('anything else is a plain failure', () => {
    expect(attachmentErrorMessage({ status: 'FETCH_ERROR', error: 'x' })).toBe(
      ATTACHMENT_ERROR_MESSAGES.failed,
    );
    expect(attachmentErrorMessage(undefined)).toBe(ATTACHMENT_ERROR_MESSAGES.failed);
  });
});

describe('isRetryableChatError (D-139)', () => {
  it('offers asking again when it may work', () => {
    for (const code of ['busy', 'no_answer', 'failed', 'something_new', undefined])
      expect(isRetryableChatError(code)).toBe(true);
  });

  it('not when the same question would fail the same way', () => {
    for (const code of ['context_too_long', 'request_too_large', 'refused', 'unavailable'])
      expect(isRetryableChatError(code)).toBe(false);
  });
});

describe('messageTooLongNote', () => {
  it("is the server's own limit", () => {
    const limit = /MaxMessageLength = ([\d_]+);/.exec(chatbotControllerCs);
    expect(limit).not.toBeNull();
    expect(Number(limit![1].replace(/_/g, ''))).toBe(MAX_MESSAGE_LENGTH);
  });

  it('says nothing up to the limit, and how long the message is past it', () => {
    expect(messageTooLongNote('a'.repeat(MAX_MESSAGE_LENGTH))).toBeNull();
    expect(messageTooLongNote('a'.repeat(MAX_MESSAGE_LENGTH + 1))).toBe(
      '訊息太長（20,001／20,000 字），長文請存成文字檔附加',
    );
  });
});
