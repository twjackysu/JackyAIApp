import { StreamingStatus } from '@/hooks/useChatStreaming';
import { AgentStatus } from '../types';

export const useAgentStatus = (streamingStatus: StreamingStatus) => {
  const getAgentStatus = (): AgentStatus => {
    if (streamingStatus.isLoading) {
      return 'working';
    }
    return 'online';
  };

  const getAgentStatusText = () => {
    if (streamingStatus.isLoading) {
      return streamingStatus.statusText || '處理中...';
    }
    return '線上';
  };

  const getStatusColor = () => {
    if (streamingStatus.isLoading) {
      return 'info.main'; // 藍色表示工作中
    }
    return 'success.main'; // 綠色表示線上
  };

  return {
    agentStatus: getAgentStatus(),
    statusText: getAgentStatusText(),
    statusColor: getStatusColor(),
  };
};