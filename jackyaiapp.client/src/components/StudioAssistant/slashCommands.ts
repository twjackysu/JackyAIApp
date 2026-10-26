import { BRIEFING_PROMPT, FIND_MY_WEAK_SPOTS, READ_DASHBOARD_PROMPT } from './askStudio';
import { dictionaryWordOf, MAKE_DASHBOARD, RECORD_TRADE, WRITE_WITH_MY_WORDS } from './studioChat';

import type { DashboardSummary } from '@/apis/dashboardApis/types';
import type { SlashCommand } from '@/components/aiChat/utils/slashCommands';

import { matchSlashCommands } from '@/components/aiChat/utils/slashCommands';

export type { SlashCommand } from '@/components/aiChat/utils/slashCommands';
export { isSlashDraft } from '@/components/aiChat/utils/slashCommands';

/**
 * Studio AI's ready-made requests, typed as `/` in its box (D-111; the matching and the type are the chat's,
 * `aiChat/utils/slashCommands`). The same requests another AI gets from the site's MCP server as prompts (Claude
 * Code's `/mcp__jacky-studio__daily_briefing`, the server's `SitePrompt`s), so each also answers to that prompt's name.
 */
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
    key: 'my_weak_spots',
    label: '找出我的弱點',
    ask: FIND_MY_WEAK_SPOTS,
    kind: 'command',
    words: ['弱點', '錯字', '加強', 'weak', 'mistakes'],
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
    // A native connector's read (`jira_issues`, D-130), not one of the site MCP server's prompts.
    key: 'my_jira_issues',
    label: '我的工作待辦',
    ask: '我手上有哪些還沒完成的 Jira 票？先講最該處理的',
    kind: 'command',
    words: ['jira', 'issue', '待辦', '票', 'ticket'],
  },
  {
    // Studio AI's own tool, not one of the site MCP server's prompts (D-124): the model asks what the server is for.
    key: 'find_mcp_server',
    label: '找 MCP server',
    ask: '幫我在 MCP 官方目錄找一個 MCP server，讓你能做更多事',
    kind: 'command',
    words: ['mcp', 'server', 'connector', '外掛', '擴充'],
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
 * explaining that word; on /portfolio, recording a trade; on the dashboards list, making a new one; on one of the reader's own dashboards, reading it (read_dashboard reads only the reader's
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
  // The model asks which holding, how many and at what price, or for the trade confirmation's screenshot.
  if (path === '/portfolio' || path.startsWith('/portfolio/'))
    return [
      {
        key: 'record_trade',
        label: '記一筆交易',
        ask: RECORD_TRADE,
        kind: 'command',
        words: ['記', '交易', '買', '賣', '加碼', 'trade', 'record', 'buy', 'sell'],
      },
    ];
  // Studio AI hands the dashboard to /data-analysis's assistant, which has the data tools (D-095).
  if (path === '/dashboards')
    return [
      {
        key: 'make_dashboard',
        label: '做一個新的 dashboard',
        ask: MAKE_DASHBOARD,
        kind: 'command',
        words: ['做', '新的', '建立', 'dashboard', '儀表板', 'make', 'new', 'create'],
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

/**
 * The commands for what is typed on page `path`: the page's own first (`pageSlashCommands`), then the site's,
 * matched as every assistant's are (`matchSlashCommands`).
 */
export function slashCommandsFor(
  draft: string,
  path = '',
  dashboards: readonly PageDashboard[] = [],
): SlashCommand[] {
  return matchSlashCommands(draft, [...pageSlashCommands(path, dashboards), ...SLASH_COMMANDS]);
}
