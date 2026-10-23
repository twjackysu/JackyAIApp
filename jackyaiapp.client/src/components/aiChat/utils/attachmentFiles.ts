import {
  ATTACHMENT_ACCEPT,
  ATTACHMENT_ERROR_MESSAGES,
  MAX_ATTACHMENT_BYTES,
} from '@/apis/chatbotApis/chatErrors';

const ACCEPTED_EXTENSIONS = ATTACHMENT_ACCEPT.split(',');

/** Why a picked file will not be uploaded at all, or undefined when it can be. */
export const rejectBeforeUpload = (file: File): string | undefined => {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!ACCEPTED_EXTENSIONS.includes(extension)) return ATTACHMENT_ERROR_MESSAGES.unsupported;
  if (file.size > MAX_ATTACHMENT_BYTES) return ATTACHMENT_ERROR_MESSAGES.tooLarge;
  if (file.size === 0) return ATTACHMENT_ERROR_MESSAGES.unreadable;
  return undefined;
};

// Object URLs made for previews and not yet revoked. Not every environment can make them (tests);
// a chip then shows its icon instead.
const livePreviewUrls = new Set<string>();

export const createPreviewUrl = (file: File) => {
  if (typeof URL.createObjectURL !== 'function') return undefined;
  const url = URL.createObjectURL(file);
  livePreviewUrls.add(url);
  return url;
};

export const revokePreviewUrl = (url: string | undefined) => {
  if (!url || !livePreviewUrls.delete(url)) return;
  URL.revokeObjectURL(url);
};

/** Lets go of every local preview still held (when the panel goes away). */
export const revokeAllPreviewUrls = () => livePreviewUrls.forEach(revokePreviewUrl);
