import AutoAwesomeIcon from '@mui/icons-material/AutoAwesomeOutlined';
import AutoStoriesIcon from '@mui/icons-material/AutoStoriesOutlined';
import DashboardIcon from '@mui/icons-material/DashboardOutlined';
import EditNoteIcon from '@mui/icons-material/EditNoteOutlined';
import EventIcon from '@mui/icons-material/EventOutlined';
import ExploreIcon from '@mui/icons-material/ExploreOutlined';
import ExtensionIcon from '@mui/icons-material/ExtensionOutlined';
import PieChartIcon from '@mui/icons-material/PieChartOutlined';
import PsychologyIcon from '@mui/icons-material/PsychologyOutlined';
import QuizIcon from '@mui/icons-material/QuizOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChartOutlined';

import { BRIEFING_PROMPT, READ_DASHBOARD_PROMPT } from './askStudio';
import QuizCardView from './QuizCardView';

import type { AiChatConfig, AiChatSuggestion } from '@/components/aiChat/config';

/**
 * Studio AI, the assistant on every page (docs/ai-native/design/05 §1): the server's `studio` profile,
 * which knows the site's pages and the one the reader is on, sends them where a task is done, and
 * uses their own connectors.
 */

/** Pages with an assistant of their own: Studio AI's button stays out of its way there. */
const OWN_ASSISTANT = ['/finance', '/data-analysis'];
/** Pages that fill the screen with something else, or ask the reader one thing (another AI's sign-in). */
const FULL_SCREEN = ['/slideshow', '/oauth'];

const on = (path: string, prefix: string) => path === prefix || path.startsWith(`${prefix}/`);

/** Whether Studio AI's button is shown on `path`. */
export function studioShownOn(path: string): boolean {
  return ![...OWN_ASSISTANT, ...FULL_SCREEN].some((p) => on(path, p));
}

const ENGLISH: AiChatSuggestion[] = [
  { text: '考我今天該複習的單字', icon: QuizIcon },
  { text: '幫我查 serendipity 是什麼意思', icon: AutoStoriesIcon },
];
/** The reader's own words (saved_words): writing with them is what the repository page cannot do itself. */
export const WRITE_WITH_MY_WORDS = '用我單字庫裡最近存的字寫一段短文';
const REPOSITORY: AiChatSuggestion[] = [
  { text: WRITE_WITH_MY_WORDS, icon: EditNoteIcon },
  { text: '考我今天該複習的單字', icon: QuizIcon },
];
/** The market side of the reader's own holdings: answered by /finance's assistant, handed the holdings (D-097). */
export const MY_HOLDINGS_MARKET = '我的持股最近外資在買還是賣？';
const PORTFOLIO: AiChatSuggestion[] = [
  { text: '幫我看看我的持股，哪幾檔虧最多？', icon: PieChartIcon },
  // Studio AI reads the holdings, then hands the question to /finance's assistant, which has the market data (D-097).
  { text: MY_HOLDINGS_MARKET, icon: ShowChartIcon },
  // The model asks which holding, how many and at what price, or for the trade confirmation's screenshot.
  { text: '幫我記一筆今天的交易', icon: EditNoteIcon },
];
/** A word's dictionary page as the server's `open_page` makes it: letters, an apostrophe or a hyphen. */
const DICTIONARY_WORD = /^\/english\/dictionary\/([a-z][a-z'-]{0,29})$/i;

/** The English word whose dictionary page `path` is, lowercase, or undefined. */
export function dictionaryWordOf(path: string): string | undefined {
  let decoded: string;
  try {
    decoded = decodeURIComponent(path);
  } catch {
    return undefined;
  }
  return DICTIONARY_WORD.exec(decoded)?.[1].toLowerCase();
}

/** On a word's dictionary page: about that word, which the reader is reading. */
const wordSuggestions = (word: string): AiChatSuggestion[] => [
  { text: `把 ${word} 加入我的單字庫`, icon: AutoStoriesIcon },
  { text: `用 ${word} 造三個例句，附中文翻譯`, icon: AutoStoriesIcon },
];
/** On one dashboard (`/dashboards/{id}`), which the server's prompt names as "this dashboard". */
const DASHBOARD: AiChatSuggestion[] = [{ text: READ_DASHBOARD_PROMPT, icon: DashboardIcon }];
/** What Studio AI remembers about the reader (`remember`, docs/ai-native/design/08). */
const WHAT_DO_YOU_REMEMBER = '你記得我什麼？';
const CONNECTORS: AiChatSuggestion[] = [
  { text: '怎麼讓 Claude 或 ChatGPT 使用這個網站？', icon: ExtensionIcon },
  { text: WHAT_DO_YOU_REMEMBER, icon: PsychologyIcon },
];
const COMMON: AiChatSuggestion[] = [
  { text: BRIEFING_PROMPT, icon: AutoAwesomeIcon },
  { text: '這個網站可以做哪些事？', icon: ExploreIcon },
  { text: '我今天有什麼行程？', icon: EventIcon },
  { text: '怎麼讓你使用我自己的 MCP server？', icon: ExtensionIcon },
];

/** A dashboard of the reader's, as the /dashboards list gives it. */
export interface StudioDashboard {
  name: string;
  isOwner: boolean;
}

/** A name longer than this is not put in a suggestion (read_dashboard finds a dashboard by its whole name). */
const MAX_SUGGESTED_NAME = 30;

/** Making a whole dashboard: Studio AI hands it to /data-analysis's assistant, which has the data tools (D-095). */
export const MAKE_DASHBOARD = '幫我做一個業績總覽 dashboard（3 到 4 張圖），存完打開';

/**
 * On the dashboards list: reading the first of the reader's own (read_dashboard reads only those), then making a
 * new one.
 */
function dashboardsSuggestions(dashboards: readonly StudioDashboard[]): AiChatSuggestion[] {
  const name = dashboards.find((d) => d.isOwner && d.name.trim())?.name.trim();
  const make: AiChatSuggestion = { text: MAKE_DASHBOARD, icon: DashboardIcon };
  return name && name.length <= MAX_SUGGESTED_NAME
    ? [{ text: `幫我解讀「${name}」這個 dashboard`, icon: DashboardIcon }, make]
    : [make];
}

/**
 * Suggestions for the page the reader is on: two about it, if it has its own, then the site's. `dashboards`
 * is the reader's /dashboards list, once read.
 */
export function studioSuggestionsFor(
  path: string,
  dashboards: readonly StudioDashboard[] = [],
): AiChatSuggestion[] {
  const word = dictionaryWordOf(path);
  const own = word
    ? wordSuggestions(word)
    : path === '/dashboards'
      ? dashboardsSuggestions(dashboards)
      : on(path, '/english/repository')
        ? REPOSITORY
        : on(path, '/english')
          ? ENGLISH
          : on(path, '/portfolio')
            ? PORTFOLIO
            : on(path, '/connectors')
              ? CONNECTORS
              : path.startsWith('/dashboards/')
                ? DASHBOARD
                : [];
  return [...own, ...COMMON].slice(0, 4);
}

/** Follow-ups that take the reader to a page (open_page with go), by that page: not offered on it. */
const GO_TO: Readonly<Record<string, string>> = {
  帶我去複習: '/english/review',
  帶我去每日挑戰: '/english/daily',
};

/** What to ask next, by the site tool the answer used (the server's names, `SiteToolSet`). */
const STUDIO_FOLLOW_UPS: NonNullable<AiChatConfig['followUps']> = [
  { tools: ['look_up_word'], texts: ['把這個字加入我的單字庫', '用這個字造三個例句'] },
  { tools: ['add_word_to_repository'], texts: ['我今天還有哪些字要複習？'] },
  { tools: ['learning_progress'], texts: ['考我幾題', '帶我去複習', '帶我去每日挑戰'] },
  { tools: ['review_quiz'], texts: ['再考我 3 題', '帶我去複習'] },
  { tools: ['saved_words'], texts: ['換個主題再寫一段', '考我幾題'] },
  {
    tools: ['portfolio_summary'],
    texts: ['哪一檔佔比最高？風險集中嗎？', MY_HOLDINGS_MARKET, '我的資產配置偏重哪裡？'],
  },
  { tools: ['update_holding'], texts: ['我的投資組合現在怎麼樣？'] },
  { tools: ['remember', 'forget'], texts: [WHAT_DO_YOU_REMEMBER] },
  { tools: ['google_calendar_events'], texts: ['明天呢？', '這週哪天比較空？'] },
  {
    tools: ['read_dashboard'],
    texts: ['哪張圖的變化最大？為什麼？', '有沒有看起來不正常的數字？'],
  },
];

/** Studio AI's follow-ups on `path`: none that would take the reader to the page they are on. */
export function studioFollowUpsFor(path: string): NonNullable<AiChatConfig['followUps']> {
  return STUDIO_FOLLOW_UPS.map((rule) => ({
    ...rule,
    texts: rule.texts.filter((t) => !(GO_TO[t] && on(path, GO_TO[t]))),
  }));
}

/** Studio AI's config on `path`; the path goes with every message as the page context. */
export function studioChatConfig(
  path: string,
  dashboards: readonly StudioDashboard[] = [],
): AiChatConfig {
  return {
    profile: 'studio',
    title: 'Studio AI',
    greetingTitle: '我是 Studio AI，想做什麼？',
    greeting:
      '告訴我你想做的事，我帶你到對的地方，也能使用你在 Connectors 接上的工具（會改變東西的動作會先問你）。打頁面名稱、dashboard 名稱或一個英文單字，可以不經 AI 直接前往；打 / 看現成指令（今日簡報、考我單字…）；在頁面上選取一段字，也能直接叫我解釋或翻譯。說「記住…」，我就會記得你的目標與偏好。',
    examples: ['這個網站可以做哪些事？', '幫我查一個英文單字', '我的投資組合現在怎麼樣？'],
    suggestions: studioSuggestionsFor(path, dashboards),
    connectedLabel: '全站助理',
    signedOutPlaceholder: '登入後即可使用 Studio AI',
    inputPlaceholder: {
      narrow: '想做什麼？打 / 看指令',
      wide: '想做什麼？例如：幫我查 serendipity；打 / 看現成指令（Enter 送出）',
    },
    disclaimer: 'AI 回答可能有誤；會改變資料的動作都會先請你確認。',
    pageContext: { path: path.slice(0, 100) },
    followUps: studioFollowUpsFor(path),
    // The review quiz is answered on its card (review_quiz).
    cardRenderers: { quiz: QuizCardView },
  };
}

/** Whether a key press is the shortcut that opens Studio AI (⌘K, or Ctrl+K). */
export const isStudioShortcut = (
  e: Pick<KeyboardEvent, 'key' | 'metaKey' | 'ctrlKey' | 'altKey'>,
) => (e.metaKey || e.ctrlKey) && !e.altKey && e.key.toLowerCase() === 'k';
