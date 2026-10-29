import { createContext, useContext } from 'react';

/**
 * Where an MCP App's `ui/message` goes (D-204): the box of the assistant whose answer shows the app, once the reader
 * pressed 放進輸入框 under it. It is never sent by itself — the app is a third party's code, so its words reach the
 * model only as the reader's message, and as text that is not theirs (`quotesPageText`). Null where no assistant
 * takes it: the host then tells the app it does not support `ui/message`.
 */
export const AppDraftContext = createContext<((text: string) => void) | null>(null);

export const useAppDraft = () => useContext(AppDraftContext);

/**
 * The box with an app's text added: after what was typed, on a line of its own. Text that would make the box a `/`
 * draft (Enter runs the first ready-made request listed, which may navigate) is put in 「」, so one press of Enter
 * after 放進輸入框 still asks the AI the app's words.
 */
export function draftWithAppText(typed: string, text: string): string {
  const kept = typed.trimEnd();
  if (kept) return `${kept}\n${text}`;
  return text.trimStart().startsWith('/') ? `「${text}」` : text;
}
