import AutoAwesomeIcon from '@mui/icons-material/AutoAwesomeOutlined';
import AutoStoriesIcon from '@mui/icons-material/AutoStoriesOutlined';
import EventIcon from '@mui/icons-material/EventOutlined';
import ExploreIcon from '@mui/icons-material/ExploreOutlined';
import ExtensionIcon from '@mui/icons-material/ExtensionOutlined';
import PieChartIcon from '@mui/icons-material/PieChartOutlined';

import { BRIEFING_PROMPT } from './askStudio';

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
  { text: '幫我查 serendipity 是什麼意思', icon: AutoStoriesIcon },
  { text: '我想複習今天該複習的單字', icon: AutoStoriesIcon },
];
const PORTFOLIO: AiChatSuggestion[] = [
  { text: '幫我看看我的持股，哪幾檔虧最多？', icon: PieChartIcon },
  { text: '我的資產配置現在偏重哪裡？', icon: PieChartIcon },
];
const CONNECTORS: AiChatSuggestion[] = [
  { text: '怎麼讓 Claude 或 ChatGPT 使用這個網站？', icon: ExtensionIcon },
];
const COMMON: AiChatSuggestion[] = [
  { text: BRIEFING_PROMPT, icon: AutoAwesomeIcon },
  { text: '這個網站可以做哪些事？', icon: ExploreIcon },
  { text: '我今天有什麼行程？', icon: EventIcon },
  { text: '怎麼讓你使用我自己的 MCP server？', icon: ExtensionIcon },
];

/** Suggestions for the page the reader is on: two about it, if it has its own, then the site's. */
export function studioSuggestionsFor(path: string): AiChatSuggestion[] {
  const own = on(path, '/english')
    ? ENGLISH
    : on(path, '/portfolio')
      ? PORTFOLIO
      : on(path, '/connectors')
        ? CONNECTORS
        : [];
  return [...own, ...COMMON].slice(0, 4);
}

/** What to ask next, by the site tool the answer used (the server's names, `SiteToolSet`). */
const STUDIO_FOLLOW_UPS: AiChatConfig['followUps'] = [
  { tools: ['look_up_word'], texts: ['把這個字加入我的單字庫', '用這個字造三個例句'] },
  { tools: ['add_word_to_repository'], texts: ['我今天還有哪些字要複習？'] },
  { tools: ['learning_progress'], texts: ['帶我去複習', '帶我去每日挑戰'] },
  { tools: ['portfolio_summary'], texts: ['哪一檔佔比最高？風險集中嗎？', '帶我去投資組合頁'] },
  { tools: ['google_calendar_events'], texts: ['明天呢？', '這週哪天比較空？'] },
];

/** Studio AI's config on `path`; the path goes with every message as the page context. */
export function studioChatConfig(path: string): AiChatConfig {
  return {
    profile: 'studio',
    title: 'Studio AI',
    greetingTitle: '我是 Studio AI，想做什麼？',
    greeting:
      '我在網站的每一頁都在：告訴我你想做的事，我帶你到對的地方，也能使用你在 Connectors 接上的工具（會改變東西的動作會先問你）。',
    examples: ['這個網站可以做哪些事？', '幫我查一個英文單字', '我的投資組合現在怎麼樣？'],
    suggestions: studioSuggestionsFor(path),
    connectedLabel: '全站助理',
    signedOutPlaceholder: '登入後即可使用 Studio AI',
    inputPlaceholder: {
      narrow: '想做什麼？',
      wide: '想做什麼？例如：幫我查 serendipity（Enter 送出，Shift+Enter 換行）',
    },
    disclaimer: 'AI 回答可能有誤；會改變資料的動作都會先請你確認。',
    pageContext: { path: path.slice(0, 100) },
    followUps: STUDIO_FOLLOW_UPS,
  };
}

/** Whether a key press is the shortcut that opens Studio AI (⌘K, or Ctrl+K). */
export const isStudioShortcut = (
  e: Pick<KeyboardEvent, 'key' | 'metaKey' | 'ctrlKey' | 'altKey'>,
) => (e.metaKey || e.ctrlKey) && !e.altKey && e.key.toLowerCase() === 'k';
