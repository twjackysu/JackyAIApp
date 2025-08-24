export interface Message {
  id: string;
  content: string;
  isUser: boolean;
  timestamp: Date;
}

export interface Position {
  x: number;
  y: number;
}

export type AgentStatus = 'online' | 'working' | 'offline';