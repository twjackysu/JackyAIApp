import { describe, expect, it } from 'vitest';

import {
  UPLOAD_FAILED_MESSAGE,
  UPLOAD_TOO_LARGE_MESSAGE,
  uploadErrorMessage,
} from './uploadErrorMessage';

const envelope = (status: number, code: number, message: string) => ({
  status,
  data: { error: { code, message } },
});

describe('uploadErrorMessage', () => {
  it("shows the server's message when the upload is refused for load (429)", () => {
    expect(
      uploadErrorMessage(envelope(429, 1042901, '目前上傳的資料量已達上限，請稍後再試。')),
    ).toBe('目前上傳的資料量已達上限，請稍後再試。');
  });

  it("shows the server's message for a refused shape or size", () => {
    expect(uploadErrorMessage(envelope(400, 1040000, '每份資料最多 200 欄。'))).toBe(
      '每份資料最多 200 欄。',
    );
    expect(uploadErrorMessage(envelope(413, 1041300, '資料太大（上限約 32 MB）'))).toBe(
      '資料太大（上限約 32 MB）',
    );
  });

  it('explains a bare 413 from the web server', () => {
    expect(uploadErrorMessage({ status: 413, data: '' })).toBe(UPLOAD_TOO_LARGE_MESSAGE);
  });

  it('stays generic for server errors and network failures', () => {
    expect(uploadErrorMessage(envelope(500, 1050000, 'An unexpected error occurred.'))).toBe(
      UPLOAD_FAILED_MESSAGE,
    );
    expect(uploadErrorMessage({ status: 'FETCH_ERROR', error: 'TypeError' })).toBe(
      UPLOAD_FAILED_MESSAGE,
    );
    expect(uploadErrorMessage(undefined)).toBe(UPLOAD_FAILED_MESSAGE);
  });
});
