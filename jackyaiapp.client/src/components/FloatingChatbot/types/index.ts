import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

export interface Message {
  id: string;
  content: string;
  isUser: boolean;
  timestamp: Date;
  // 新增：解析後的區塊結構（只對 bot 消息有效）
  parsedBlocks?: ParsedBlock[];
}

export interface Position {
  x: number;
  y: number;
}

export type AgentStatus = 'online' | 'working' | 'offline';
