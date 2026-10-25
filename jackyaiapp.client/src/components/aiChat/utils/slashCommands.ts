/**
 * An assistant's ready-made requests, typed as `/` in its box (D-111, D-114): `/` lists them, `/簡報` or `/quiz`
 * narrows them, and a press sends the request as if typed. Studio AI's are `StudioAssistant/slashCommands`; an app's
 * own assistant has its list in its config (`AiChatConfig.slashCommands`). Unlike a quick action these ask the AI,
 * so they cost what a message costs.
 */
export interface SlashCommand {
  /** Unique among the commands; the site MCP server prompt's name where there is one. */
  key: string;
  label: string;
  /** What is sent. */
  ask: string;
  /** In a new conversation (the briefing, as the home page sends it). */
  fresh?: boolean;
  kind: 'command';
  /** What a reader might type after the slash, lowercase, besides the label. */
  words: readonly string[];
}

/** Whether what is typed asks for the commands: it starts with a slash (after any spaces). */
export const isSlashDraft = (draft: string) => draft.trimStart().startsWith('/');

/** Shorter than this, what follows the slash is matched against the labels only ("/a" is no command's word). */
const MIN_WORD_QUERY = 2;

/**
 * Which of `commands` a draft asks for: all of them for a bare `/`; else those whose label holds what follows the
 * slash, or (from two characters) whose key or one of whose words does. None when the draft does not start with
 * `/`, or what follows is a path (another slash) or longer than a command's name would be: these ask the AI, so
 * they are offered only for what plainly names one.
 */
export function matchSlashCommands(
  draft: string,
  commands: readonly SlashCommand[],
): SlashCommand[] {
  if (!isSlashDraft(draft)) return [];
  const q = draft.trimStart().slice(1).trim().toLowerCase();
  if (q.length === 0) return [...commands];
  if (q.length > 20 || q.includes('/')) return [];
  const byWords = q.length >= MIN_WORD_QUERY;
  return commands.filter(
    (c) =>
      c.label.toLowerCase().includes(q) ||
      (byWords && (c.key.includes(q) || c.words.some((w) => w.includes(q)))),
  );
}
