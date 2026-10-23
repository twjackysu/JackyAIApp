import { useEffect, useRef, useState } from 'react';

import { isImageFile, prepareChatImage, UnreadableImageError } from '../prepareChatImage';
import { PendingAttachment } from '../types';
import {
  createPreviewUrl,
  rejectBeforeUpload,
  revokeAllPreviewUrls,
  revokePreviewUrl,
} from '../utils/attachmentFiles';

import {
  useDeleteChatAttachmentMutation,
  useUploadChatAttachmentMutation,
} from '@/apis/chatbotApis';
import {
  ATTACHMENT_ERROR_MESSAGES,
  attachmentErrorMessage,
  MAX_ATTACHMENTS,
} from '@/apis/chatbotApis/chatErrors';
import { ChatAttachment } from '@/apis/chatbotApis/types';

/**
 * Files picked for the next message. Each uploads as soon as it is picked, so the server has read
 * it by the time the message is sent.
 */
export function usePendingAttachments() {
  const [pending, setPending] = useState<PendingAttachment[]>([]);
  const [uploadAttachment] = useUploadChatAttachmentMutation();
  const [deleteAttachment] = useDeleteChatAttachmentMutation();
  const nextKey = useRef(0);
  // Chips removed while their upload was still running: the upload is deleted once it lands.
  const removedWhileUploading = useRef(new Set<string>());

  const update = (key: string, patch: Partial<PendingAttachment>) =>
    setPending((prev) => prev.map((a) => (a.key === key ? { ...a, ...patch } : a)));

  const add = (files: File[]) => {
    // A failed chip is only there to say why; it does not take up one of the slots — one already
    // there, or one turned down in this same pick.
    let taken = pending.filter((a) => !a.error).length;
    const added: PendingAttachment[] = files.map((file) => {
      const key = `attachment-${nextKey.current++}`;
      const error =
        taken >= MAX_ATTACHMENTS ? ATTACHMENT_ERROR_MESSAGES.tooMany : rejectBeforeUpload(file);
      if (!error) taken += 1;
      const previewUrl = !error && isImageFile(file) ? createPreviewUrl(file) : undefined;
      return { key, fileName: file.name, error, previewUrl };
    });
    setPending((prev) => [...prev, ...added]);

    added.forEach((chip, i) => {
      if (chip.error) return;
      prepareChatImage(files[i])
        .then((file) => uploadAttachment(file).unwrap())
        .then((res) => {
          if (removedWhileUploading.current.delete(chip.key)) {
            deleteAttachment(res.data.id);
          } else {
            update(chip.key, { attachment: res.data });
          }
        })
        .catch((e: unknown) => {
          removedWhileUploading.current.delete(chip.key);
          revokePreviewUrl(chip.previewUrl);
          const error =
            e instanceof UnreadableImageError
              ? ATTACHMENT_ERROR_MESSAGES.unreadable
              : attachmentErrorMessage(e);
          update(chip.key, { error, previewUrl: undefined });
        });
    });
  };

  const remove = (key: string) => {
    const chip = pending.find((a) => a.key === key);
    if (!chip) return;
    if (chip.attachment) deleteAttachment(chip.attachment.id);
    else if (!chip.error) removedWhileUploading.current.add(key);
    revokePreviewUrl(chip.previewUrl);
    setPending((prev) => prev.filter((a) => a.key !== key));
  };

  /** The uploaded files, handed over to a message being sent; the chips go. The sent message
   *  shows its images from the server from here on. */
  const takeForMessage = (): ChatAttachment[] => {
    const attachments = pending.flatMap((a) => (a.attachment ? [a.attachment] : []));
    pending.forEach((a) => revokePreviewUrl(a.previewUrl));
    setPending([]);
    return attachments;
  };

  // The local previews still held are let go when the panel goes away, too.
  useEffect(() => () => revokeAllPreviewUrls(), []);

  return {
    pending,
    add,
    remove,
    takeForMessage,
    /** Some file has not finished uploading (nor failed). */
    uploading: pending.some((a) => !a.attachment && !a.error),
  };
}
