/** One thing the reader asked Studio AI to remember (server: `AssistantMemoryDto`, docs/ai-native/design/08). */
export interface AssistantMemory {
  id: string;
  /** The note in the reader's words, one line. */
  text: string;
  /** When it was kept (UTC, ISO). */
  createdAt: string;
}

/** The reader's memories, newest first, and how many they may keep (`AssistantMemoryListDto`). */
export interface AssistantMemoryList {
  memories: AssistantMemory[];
  limit: number;
}
