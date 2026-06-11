export interface DifyEventContext {
  setStatus: (event: string, text: string) => void;
  appendContent: (delta: string) => void;
  replaceContent: (text: string) => void;
  complete: (conversationId?: string) => void;
  fail: (error: string) => void;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type DifyEvent = Record<string, any>;

const handlers: Record<string, (data: DifyEvent, ctx: DifyEventContext) => void> = {
  workflow_started: (_data, ctx) => {
    ctx.setStatus('workflow_started', '工作流程已開始');
  },

  node_started: (data, ctx) => {
    const title = data.data?.title || '節點';
    ctx.setStatus('node_started', `正在執行: ${title}`);
  },

  node_finished: (data, ctx) => {
    const title = data.data?.title || '節點';
    const status = data.data?.status || 'unknown';
    const emoji = status === 'succeeded' ? '✅' : status === 'failed' ? '❌' : '⏸️';
    if (data.data?.outputs?.text) {
      ctx.replaceContent(data.data.outputs.text);
    }
    ctx.setStatus('node_finished', `${emoji} ${title} 已完成`);
  },

  agent_log: (data, ctx) => {
    const label = data.data?.label || '';
    const status = data.data?.status || '';
    const emoji =
      status === 'start' ? '🔄' : status === 'success' ? '✅' : status === 'error' ? '❌' : '';
    ctx.setStatus('agent_log', emoji ? `${emoji} ${label}` : label);

    if (status === 'success' && data.data?.data?.output) {
      const output = data.data.data.output;
      if (typeof output === 'string') {
        ctx.replaceContent(output);
      } else if (output.llm_response) {
        ctx.replaceContent(output.llm_response);
      }
    }
  },

  message: (data, ctx) => {
    ctx.appendContent(data.answer || '');
    ctx.setStatus('message', '正在生成回應...');
  },

  // message_end: 500ms delay then complete + early return handled in the caller loop.
  // We set status here; the caller checks for this event to trigger the delayed complete.
  message_end: (_data, ctx) => {
    ctx.setStatus('message_end', '回應生成完成');
  },

  workflow_finished: (data, ctx) => {
    const status = data.data?.status || 'unknown';
    const emoji = status === 'succeeded' ? '🎉' : status === 'failed' ? '💥' : '⏸️';
    ctx.setStatus('workflow_finished', `${emoji} 工作流程已完成`);
    if (data.data?.outputs?.text) {
      ctx.replaceContent(data.data.outputs.text);
    }
  },

  error: (data, ctx) => {
    ctx.fail(data.message || 'Unknown error');
  },

  ping: () => {
    // keep-alive, no state update needed
  },
};

export function handleDifyEvent(
  data: DifyEvent,
  ctx: DifyEventContext,
): 'complete' | 'error' | 'continue' {
  const handler = handlers[data.event];

  if (data.event === 'message_end') {
    handlers.message_end(data, ctx);
    return 'complete'; // caller schedules the 500ms delay
  }

  if (data.event === 'error') {
    handlers.error(data, ctx);
    return 'error';
  }

  if (handler) {
    handler(data, ctx);
  } else {
    // unknown event — try to extract text content
    console.log('Unknown Dify event:', data.event, data);
    if (data.answer) ctx.appendContent(data.answer);
    else if (data.text) ctx.appendContent(data.text);
  }

  return 'continue';
}
