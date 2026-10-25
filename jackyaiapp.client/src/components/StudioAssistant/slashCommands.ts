import { BRIEFING_PROMPT } from './askStudio';
import { WRITE_WITH_MY_WORDS } from './studioChat';

/**
 * Studio AI's ready-made requests, typed as `/` in its box (D-111): `/` lists them, `/簡報` or `/quiz` narrows
 * them, and a press sends the request as if typed. The same requests another AI gets from the site's MCP server
 * as prompts (Claude Code's `/mcp__jacky-studio__daily_briefing`, the server's `SitePrompt`s), so each also
 * answers to that prompt's name. Unlike a quick action these ask the AI, so they cost what a message costs.
 */
export interface SlashCommand {
  /** Unique among the commands; the server prompt's name where there is one. */
  key: string;
  label: string;
  /** What is sent. */
  ask: string;
  /** In a new conversation (the briefing, as the home page's card sends it). */
  fresh?: boolean;
  kind: 'command';
  /** What a reader might type after the slash, lowercase, besides the label. */
  words: readonly string[];
}

export const SLASH_COMMANDS: readonly SlashCommand[] = [
  {
    key: 'daily_briefing',
    label: '今日簡報',
    ask: BRIEFING_PROMPT,
    fresh: true,
    kind: 'command',
    words: ['簡報', 'briefing', 'brief', 'daily', '早安', 'today'],
  },
  {
    key: 'quiz_me',
    label: '考我單字',
    ask: '考我今天該複習的單字',
    kind: 'command',
    words: ['考我', '測驗', '複習', 'quiz', 'review'],
  },
  {
    key: 'write_with_my_words',
    label: '用我的單字寫短文',
    ask: WRITE_WITH_MY_WORDS,
    kind: 'command',
    words: ['短文', '寫作', '單字', 'write', 'writing', 'words'],
  },
  {
    key: 'portfolio_checkup',
    label: '投資組合健檢',
    ask: '幫我做投資組合健檢：淨值、報酬、集中度，還有今天最需要注意的一檔',
    kind: 'command',
    words: ['健檢', '投資組合', '持股', 'portfolio', 'checkup'],
  },
  {
    key: 'learning_progress',
    label: '我的學習進度',
    ask: '我今天的學習進度如何？',
    kind: 'command',
    words: ['進度', '學習', 'progress', 'streak'],
  },
  {
    key: 'memories',
    label: '你記得我什麼？',
    ask: '你記得我什麼？',
    kind: 'command',
    words: ['記得', '記憶', '筆記', 'memory', 'remember'],
  },
];

/** Whether what is typed asks for the commands: it starts with a slash (after any spaces). */
export const isSlashDraft = (draft: string) => draft.trimStart().startsWith('/');

/** Shorter than this, what follows the slash is matched against the labels only ("/a" is no command's word). */
const MIN_WORD_QUERY = 2;

/**
 * The commands for what is typed: all of them for a bare `/`; else those whose label holds what follows the
 * slash, or (from two characters) whose prompt name or one of whose words does. None when the draft does not
 * start with `/`, or what follows is a path (another slash) or longer than a command's name would be: these
 * ask the AI, so they are offered only for what plainly names one.
 */
export function slashCommandsFor(draft: string): SlashCommand[] {
  if (!isSlashDraft(draft)) return [];
  const q = draft.trimStart().slice(1).trim().toLowerCase();
  if (q.length === 0) return [...SLASH_COMMANDS];
  if (q.length > 20 || q.includes('/')) return [];
  const byWords = q.length >= MIN_WORD_QUERY;
  return SLASH_COMMANDS.filter(
    (c) =>
      c.label.toLowerCase().includes(q) ||
      (byWords && (c.key.includes(q) || c.words.some((w) => w.includes(q)))),
  );
}
