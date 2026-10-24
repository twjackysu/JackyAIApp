import { createContext, useContext } from 'react';

import type { ToolCardRenderer } from './cards/toolCard';
import type { ChatErrorCode } from '@/apis/chatbotApis/chatErrors';
import type SvgIcon from '@mui/material/SvgIcon';

/** A suggested question under the greeting, with the icon that says what kind of question it is. */
export interface AiChatSuggestion {
  text: string;
  icon: typeof SvgIcon;
}

/**
 * What one app's assistant says about itself: everything about the chat that is not the same for
 * every app. The conversation, its stream, its history and its attachments are shared
 * (docs/ai-native/design/04); an app gives its launcher one of these.
 */
export interface AiChatConfig {
  /** The server's chat profile (IChatProfile.Key). */
  profile: string;
  /** Over the greeting of a new conversation. */
  greetingTitle: string;
  /** What the assistant can do, in a sentence or two. */
  greeting: string;
  /** Shown in turn as the one-line box's placeholder; short enough to fit it on a phone. */
  examples: readonly string[];
  /** Under the greeting, the same every time. */
  suggestions: readonly AiChatSuggestion[];
  /** The header's status while idle: what the assistant is connected to. */
  connectedLabel: string;
  /** The one-line box's placeholder for a visitor who is not signed in. */
  signedOutPlaceholder: string;
  /** The message box's placeholder: on a phone, and with room for the hint about keys. */
  inputPlaceholder: { narrow: string; wide: string };
  /** Wording of this app's own for some failures (a decline that names the app's subject). */
  errorMessages?: Partial<Record<ChatErrorCode, string>>;
  /** How this app draws kinds of tool card the chat does not (cards/toolCard), by kind. */
  cardRenderers?: Readonly<Record<string, ToolCardRenderer>>;
}

export const AiChatConfigContext = createContext<AiChatConfig | null>(null);

/** The assistant's config; only under an `AiChatConfigContext` (the launcher provides it). */
export function useAiChatConfig(): AiChatConfig {
  const config = useContext(AiChatConfigContext);
  if (!config) throw new Error('useAiChatConfig needs an AiChatConfigContext above it');
  return config;
}
