import { BRIEFING_PROMPT, READ_DASHBOARD_PROMPT } from './askStudio';
import { dictionaryWordOf, WRITE_WITH_MY_WORDS } from './studioChat';

import type { DashboardSummary } from '@/apis/dashboardApis/types';

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

/** A dashboard of the reader's, as the /dashboards list gives it: whose it is decides whether it can be read. */
export type PageDashboard = Pick<DashboardSummary, 'id' | 'isOwner'>;

/** The dashboard `/dashboards/{id}` shows, its id lowercase (ids are GUIDs), or undefined on any other page. */
function dashboardIdOf(path: string): string | undefined {
  const match = /^\/dashboards\/([^/]+)$/.exec(path);
  if (!match) return undefined;
  try {
    return decodeURIComponent(match[1]).toLowerCase();
  } catch {
    return undefined;
  }
}

/**
 * The commands of the page the reader is on, listed before the site's (D-113): on a word's dictionary page,
 * explaining that word; on one of the reader's own dashboards, reading it (read_dashboard reads only the reader's
 * own, so not on one shared with them, nor before the list has been read). The same requests as the page's
 * suggestions, so the server's prompt names "this dashboard" from the page.
 */
export function pageSlashCommands(
  path: string,
  dashboards: readonly PageDashboard[] = [],
): SlashCommand[] {
  const word = dictionaryWordOf(path);
  if (word)
    return [
      {
        key: 'explain_word',
        label: `解釋 ${word}`,
        ask: `解釋 ${word}：最常用的一兩個意思，各附一個英文例句與中文翻譯，再說一個容易跟它搞混的字`,
        kind: 'command',
        words: ['解釋', '意思', 'explain', 'meaning', word],
      },
    ];
  const id = dashboardIdOf(path);
  if (id && dashboards.some((d) => d.isOwner && d.id.toLowerCase() === id))
    return [
      {
        key: 'read_my_dashboard',
        label: '解讀這個 dashboard',
        ask: READ_DASHBOARD_PROMPT,
        // As the dashboard's own 「AI 解讀」 button sends it.
        fresh: true,
        kind: 'command',
        words: ['解讀', 'dashboard', '儀表板', '摘要', 'read', 'summary'],
      },
    ];
  return [];
}

/** Whether what is typed asks for the commands: it starts with a slash (after any spaces). */
export const isSlashDraft = (draft: string) => draft.trimStart().startsWith('/');

/** Shorter than this, what follows the slash is matched against the labels only ("/a" is no command's word). */
const MIN_WORD_QUERY = 2;

/**
 * The commands for what is typed on page `path` (the page's own first, `pageSlashCommands`): all of them for a bare `/`; else those whose label holds what follows the
 * slash, or (from two characters) whose prompt name or one of whose words does. None when the draft does not
 * start with `/`, or what follows is a path (another slash) or longer than a command's name would be: these
 * ask the AI, so they are offered only for what plainly names one.
 */
export function slashCommandsFor(
  draft: string,
  path = '',
  dashboards: readonly PageDashboard[] = [],
): SlashCommand[] {
  if (!isSlashDraft(draft)) return [];
  const commands = [...pageSlashCommands(path, dashboards), ...SLASH_COMMANDS];
  const q = draft.trimStart().slice(1).trim().toLowerCase();
  if (q.length === 0) return commands;
  if (q.length > 20 || q.includes('/')) return [];
  const byWords = q.length >= MIN_WORD_QUERY;
  return commands.filter(
    (c) =>
      c.label.toLowerCase().includes(q) ||
      (byWords && (c.key.includes(q) || c.words.some((w) => w.includes(q)))),
  );
}
