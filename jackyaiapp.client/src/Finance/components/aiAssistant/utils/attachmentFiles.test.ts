import { describe, expect, it } from 'vitest';

import { rejectBeforeUpload } from './attachmentFiles';

import { ATTACHMENT_ERROR_MESSAGES, MAX_ATTACHMENT_BYTES } from '@/apis/chatbotApis/chatErrors';

const file = (name: string, size: number) => ({ name, size }) as unknown as File;

describe('rejectBeforeUpload', () => {
  it('takes a file of a type the server reads, whatever the case of its extension', () => {
    expect(rejectBeforeUpload(file('報表.xlsx', 100))).toBeUndefined();
    expect(rejectBeforeUpload(file('截圖.PNG', 100))).toBeUndefined();
  });

  it('turns down a type it cannot read, and a name without an extension', () => {
    expect(rejectBeforeUpload(file('a.exe', 100))).toBe(ATTACHMENT_ERROR_MESSAGES.unsupported);
    expect(rejectBeforeUpload(file('README', 100))).toBe(ATTACHMENT_ERROR_MESSAGES.unsupported);
  });

  it('turns down a file over the size limit, but not one exactly at it', () => {
    expect(rejectBeforeUpload(file('a.pdf', MAX_ATTACHMENT_BYTES + 1))).toBe(
      ATTACHMENT_ERROR_MESSAGES.tooLarge,
    );
    expect(rejectBeforeUpload(file('a.pdf', MAX_ATTACHMENT_BYTES))).toBeUndefined();
  });

  it('an empty file has nothing to read', () => {
    expect(rejectBeforeUpload(file('a.pdf', 0))).toBe(ATTACHMENT_ERROR_MESSAGES.unreadable);
  });
});
