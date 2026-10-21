/** A conversation summary (title auto-generated from the first user message). */
export interface ChatConversation {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

/** A file sent (or about to be sent) with a message. The server keeps only its text. */
export interface ChatAttachment {
  id: number;
  fileName: string;
  kind: 'word' | 'excel' | 'csv' | 'text';
  /** Characters of text read out of the file (after any cut). */
  charCount: number;
  /** The file had more text than was kept. */
  truncated: boolean;
}

/** One turn (user or assistant) in a conversation. */
export interface ChatHistoryMessage {
  id: number;
  role: 'user' | 'assistant';
  content: string;
  createdAt: string;
  attachments?: ChatAttachment[];
}
