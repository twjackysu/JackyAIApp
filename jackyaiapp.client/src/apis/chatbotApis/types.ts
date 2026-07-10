/** A conversation summary (title auto-generated from the first user message). */
export interface ChatConversation {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

/** One turn (user or assistant) in a conversation. */
export interface ChatHistoryMessage {
  id: number;
  role: 'user' | 'assistant';
  content: string;
  createdAt: string;
}
