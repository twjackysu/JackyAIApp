/** A conversation summary (title auto-generated from the first user message). */
export interface ChatConversation {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * A file sent (or about to be sent) with a message. Of a text file the server keeps only its text;
 * an image or a PDF it keeps as a file, served by {@link attachmentContentUrl}.
 */
export interface ChatAttachment {
  id: number;
  fileName: string;
  kind: 'word' | 'excel' | 'csv' | 'text' | 'image' | 'pdf';
  /** Characters of text read out of the file (after any cut); 0 for an image or a PDF. */
  charCount: number;
  /** The file had more text than was kept. */
  truncated: boolean;
  /** Pages of a PDF. */
  pageCount?: number | null;
}

/** Where the stored image or PDF of an attachment is served (to its owner only). */
export const attachmentContentUrl = (id: number) => `/api/chatbot/attachments/${id}/content`;

/** One turn (user or assistant) in a conversation. */
export interface ChatHistoryMessage {
  id: number;
  role: 'user' | 'assistant';
  content: string;
  createdAt: string;
  attachments?: ChatAttachment[];
}
