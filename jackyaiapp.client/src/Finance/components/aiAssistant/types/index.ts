import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

export interface Message {
  id: string;
  content: string;
  isUser: boolean;
  timestamp: Date;
  // 新增：解析後的區塊結構（只對 bot 消息有效）
  parsedBlocks?: ParsedBlock[];
  /** The answer ran into the length limit and is cut short. */
  truncated?: boolean;
}

export type AgentStatus = 'online' | 'working' | 'offline';
