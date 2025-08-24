import { useCallback, useState } from 'react';

export interface StreamingStatus {
  isLoading: boolean;
  currentEvent: string | null;
  statusText: string;
}

export const useChatStreaming = () => {
  const [streamingStatus, setStreamingStatus] = useState<StreamingStatus>({
    isLoading: false,
    currentEvent: null,
    statusText: '',
  });

  const sendStreamingMessage = useCallback(
    async (
      message: string,
      conversationId: string | undefined,
      onMessageUpdate: (content: string) => void,
      onComplete: (conversationId?: string) => void,
      onError: (error: string) => void,
    ) => {
      setStreamingStatus({
        isLoading: true,
        currentEvent: null,
        statusText: '正在連接...',
      });

      try {
        const token = localStorage.getItem('token');

        const response = await fetch('/api/chatbot/chat', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${token}`,
            Accept: 'text/event-stream',
            'Cache-Control': 'no-cache',
          },
          body: JSON.stringify({
            message,
            conversationId,
          }),
        });

        if (!response.ok) {
          throw new Error(`HTTP error! status: ${response.status}`);
        }

        if (!response.body) {
          throw new Error('No response body');
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let accumulatedContent = '';
        let extractedConversationId: string | undefined;

        setStreamingStatus((prev) => ({
          ...prev,
          statusText: '正在處理請求...',
        }));

        while (true) {
          const { done, value } = await reader.read();

          if (done) break;

          const chunk = decoder.decode(value, { stream: true });
          const lines = chunk.split('\n');

          for (const line of lines) {
            if (line.trim()) {
              try {
                // 處理 Dify API 的 streaming 格式
                if (line.startsWith('data: ')) {
                  const data = JSON.parse(line.slice(6));

                  // 提取 conversation_id (任何事件都可能包含)
                  if (data.conversation_id && !extractedConversationId) {
                    extractedConversationId = data.conversation_id;
                  }

                  switch (data.event) {
                    case 'workflow_started':
                      setStreamingStatus((prev) => ({
                        ...prev,
                        currentEvent: 'workflow_started',
                        statusText: '工作流程已開始',
                      }));
                      break;

                    case 'node_started':
                      const nodeTitle = data.data?.title || '節點';
                      setStreamingStatus((prev) => ({
                        ...prev,
                        currentEvent: 'node_started',
                        statusText: `正在執行: ${nodeTitle}`,
                      }));
                      break;

                    case 'node_finished':
                      const finishedNodeTitle = data.data?.title || '節點';
                      const status = data.data?.status || 'unknown';
                      const statusEmoji =
                        status === 'succeeded' ? '✅' : status === 'failed' ? '❌' : '⏸️';

                      // 嘗試從 node_finished 中提取文字內容
                      if (data.data?.outputs?.text && !accumulatedContent) {
                        accumulatedContent = data.data.outputs.text;
                        onMessageUpdate(accumulatedContent);
                      }

                      setStreamingStatus((prev) => ({
                        ...prev,
                        currentEvent: 'node_finished',
                        statusText: `${statusEmoji} ${finishedNodeTitle} 已完成`,
                      }));
                      break;

                    case 'agent_log':
                      // 顯示 agent_log 的 label 作為狀態
                      const agentLabel = data.data?.label || '';
                      const agentStatus = data.data?.status || '';

                      let statusText = '';
                      if (agentStatus === 'start') {
                        statusText = `🔄 ${agentLabel}`;
                      } else if (agentStatus === 'success') {
                        statusText = `✅ ${agentLabel}`;
                      } else if (agentStatus === 'error') {
                        statusText = `❌ ${agentLabel}`;
                      } else {
                        statusText = agentLabel;
                      }

                      setStreamingStatus((prev) => ({
                        ...prev,
                        currentEvent: 'agent_log',
                        statusText: statusText,
                      }));

                      // 處理 agent_log 事件中的回應內容
                      if (data.data?.status === 'success' && data.data?.data?.output) {
                        const output = data.data.data.output;
                        // 檢查是否為 LLM 回應
                        if (typeof output === 'string') {
                          accumulatedContent = output; // 直接使用完整內容
                          onMessageUpdate(accumulatedContent);
                        } else if (output.llm_response) {
                          accumulatedContent = output.llm_response; // 使用 LLM 回應
                          onMessageUpdate(accumulatedContent);
                        }
                      }
                      break;

                    case 'message':
                      // Dify 的 message event 包含片段，需要累加
                      accumulatedContent += data.answer || '';
                      onMessageUpdate(accumulatedContent);
                      setStreamingStatus((prev) => ({
                        ...prev,
                        currentEvent: 'message',
                        statusText: '正在生成回應...',
                      }));
                      break;

                    case 'message_end':
                      setStreamingStatus((prev) => ({
                        ...prev,
                        currentEvent: 'message_end',
                        statusText: '回應生成完成',
                      }));
                      setTimeout(() => {
                        onComplete(extractedConversationId);
                      }, 500);
                      return;

                    case 'workflow_finished':
                      const workflowStatus = data.data?.status || 'unknown';
                      const workflowEmoji =
                        workflowStatus === 'succeeded'
                          ? '🎉'
                          : workflowStatus === 'failed'
                            ? '💥'
                            : '⏸️';
                      setStreamingStatus((prev) => ({
                        ...prev,
                        currentEvent: 'workflow_finished',
                        statusText: `${workflowEmoji} 工作流程已完成`,
                      }));

                      // 如果 workflow 結束但還沒有內容，從 node_finished 中提取
                      if (!accumulatedContent && data.data?.outputs?.text) {
                        accumulatedContent = data.data.outputs.text;
                        onMessageUpdate(accumulatedContent);
                      }
                      break;

                    case 'error':
                      onError(data.message || 'Unknown error');
                      return;

                    case 'ping':
                      // 保持連接，無需更新狀態
                      break;

                    default:
                      console.log('Unknown event:', data.event, data);

                      // 嘗試從未知事件中提取文字內容
                      if (data.answer) {
                        accumulatedContent += data.answer;
                        onMessageUpdate(accumulatedContent);
                      } else if (data.text) {
                        accumulatedContent += data.text;
                        onMessageUpdate(accumulatedContent);
                      }
                      break;
                  }
                }
              } catch (e) {
                // JSON 解析失敗，忽略這行（不要添加到聊天內容）
                console.warn('Failed to parse line as JSON:', line, e);
              }
            }
          }
        }

        if (!accumulatedContent) {
          onComplete(extractedConversationId);
        }
      } catch (error) {
        console.error('Streaming error:', error);
        onError(error instanceof Error ? error.message : 'Unknown error occurred');
      } finally {
        setStreamingStatus((prev) => ({
          ...prev,
          isLoading: false,
          currentEvent: null,
          statusText: '',
        }));
      }
    },
    [],
  );

  return {
    sendStreamingMessage,
    streamingStatus,
  };
};
