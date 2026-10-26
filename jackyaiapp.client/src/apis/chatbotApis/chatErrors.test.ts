import { describe, expect, it } from 'vitest';

import {
  ATTACHMENT_ERROR_MESSAGES,
  attachmentErrorMessage,
  isRetryableChatError,
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
