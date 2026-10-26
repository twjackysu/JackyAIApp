import { ToolCard } from '../cards/toolCard';
import { MessageApproval } from '../utils/approval';
import { ThinkingTrace } from '../utils/thinking';
import { ToolStep } from '../utils/toolActivity';

import { ChatAttachment } from '@/apis/chatbotApis/types';
import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

/** A file picked in the chat box, from the moment it starts uploading until it is sent. */
export interface PendingAttachment {
  /** Local key: the server id only exists once the upload is done. */
  key: string;
  fileName: string;
  /** Set once uploaded. */
  attachment?: ChatAttachment;
  /** Set when the upload failed; the chip stays to say why until it is removed. */
  error?: string;
  /** A local picture of an image file, until it is sent (an object URL, revoked with the chip). */
  previewUrl?: string;
}

/** A message sent while an answer was still being written, waiting for its turn. */
export interface QueuedMessage {
  key: string;
  text: string;
  attachments: ChatAttachment[];
  /** Goes as the first message of a new conversation (`SendOptions.fresh`). */
  fresh?: boolean;
  /** Quotes text selected on a page (`SendOptions.quotesPageText`). */
  quotesPageText?: boolean;
  /** Handed over by another assistant (`SendOptions.handedOverBy`). */
  handedOverBy?: string;
  /** Its answer is read aloud once done (`SendOptions.readAloud`). */
  readAloud?: boolean;
}

export interface Message {
  id: string;
  /** The id the server stored it under, once known — what an edit refers to. */
  serverId?: number;
  content: string;
  isUser: boolean;
  timestamp: Date;
  // 新增：解析後的區塊結構（只對 bot 消息有效）
  parsedBlocks?: ParsedBlock[];
  /** The answer ran into the length limit and is cut short. */
  truncated?: boolean;
  /** The summary of the model's thinking for this answer, this session only (see ThinkingTrace, D-136). */
  thinking?: ThinkingTrace;
  /** The tools an answer used, in order (see ToolActivity). */
  toolSteps?: ToolStep[];
  /** A reopened answer's cards, as stored with it (its tool steps are not kept). */
  cards?: ToolCard[];
  /** A write the answer is waiting on the reader to allow or refuse (see ApprovalCard). */
  approval?: MessageApproval;
  /** Files sent with a user message. */
  attachments?: ChatAttachment[];
  /** A user message another assistant handed over (D-099): that assistant's name, shown over the bubble in this
   *  session only (the server keeps the text alone, so a reopened conversation shows a plain message). */
  handedOverBy?: string;
  /** An answer that came to its end in this session (not stopped, failed or reopened): follow-ups go under it. */
  answered?: boolean;
  /** An answer to read aloud once it is `answered` (`SendOptions.readAloud`, D-120): this session only. */
  readAloud?: boolean;
  /** A user message sent as quoting page text (`SendOptions.quotesPageText`): asked again, it goes so again. */
  quotesPageText?: boolean;
  /** A user message sent in place of this stored one (an edit) before the server had stored it in turn: asked again,
   *  it is still that edit (D-139). */
  replacesServerId?: number;
  /** A failed answer that may well work asked again (the service busy, the network, a cut stream), D-139. */
  retryable?: boolean;
}

export type AgentStatus = 'online' | 'working' | 'offline';
