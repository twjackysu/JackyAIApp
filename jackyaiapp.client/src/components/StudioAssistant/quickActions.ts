/**
 * What Studio AI can do at once, while the reader is still typing, without asking the AI: a few
 * words naming a page ("複習", "portfolio") or one of the reader's dashboards ("業績") offer to go
 * there, and one English word offers its dictionary page. Pressing one goes straight there — no tokens, no usage budget, no waiting — so
 * ⌘K is also the site's command bar; anything else is still sent to the AI as typed.
 */

export interface QuickAction {
  /** Unique among the actions shown. */
  key: string;
  label: string;
  /** A path on this site. */
  path: string;
  kind: 'page' | 'dashboard' | 'word';
}

/** A dashboard the reader can open (the /dashboards list: their own and those shared with them). */
export interface QuickDashboard {
  id: string;
  name: string;
}

interface QuickPage {
  path: string;
  title: string;
  /** What a reader might type for it, lowercase: its title, its English name, what is on it. */
  words: readonly string[];
}

/**
 * The site's pages, kept in step with the server's list for Studio AI's prompt
 * (JackyAIApp.Server/Services/Assistant/Studio/SitePages.cs; `quickActions.test.ts` checks every
 * page there is here). The home page is left out: the site's name in the corner goes there.
 */
export const QUICK_PAGES: readonly QuickPage[] = [
  {
    path: '/portfolio',
    title: '投資組合',
    words: ['投資組合', '持股', '資產', '淨值', 'portfolio', 'wealth'],
  },
  {
    path: '/finance',
    title: 'Stock Intelligence 台股分析',
    words: ['台股', '股票', '股市', '大盤', 'stock', 'finance'],
  },
  { path: '/english', title: '英文學習', words: ['英文', '英文學習', 'english'] },
  { path: '/english/dictionary', title: '字典', words: ['字典', '查單字', 'dictionary'] },
  {
    path: '/english/repository',
    title: '我的單字庫',
    words: ['單字庫', '我的單字', 'repository', 'words'],
  },
  {
    path: '/english/exam',
    title: '測驗',
    words: ['測驗', '考試', '克漏字', '聽力', '閱讀', '寫作', '聽寫', 'exam', 'quiz', 'test'],
  },
  { path: '/english/daily', title: '每日挑戰', words: ['每日挑戰', '挑戰', 'daily', 'challenge'] },
  { path: '/english/review', title: '複習', words: ['複習', '單字卡', 'review', 'flashcards'] },
  {
    path: '/english/stats',
    title: '學習統計',
    words: ['學習統計', '統計', '連續天數', '排行榜', 'stats', 'streak', 'xp'],
  },
  { path: '/jobs', title: '找工作', words: ['找工作', '工作', '職缺', 'jobs', '104', 'cake'] },
  {
    path: '/data-analysis',
    title: '資料分析',
    words: ['資料分析', '資料', '分析', 'data', 'analysis'],
  },
  { path: '/dashboards', title: 'Dashboards', words: ['dashboard', 'dashboards', '儀表板'] },
  {
    path: '/connectors',
    title: 'Connectors',
    words: ['connectors', 'connector', 'mcp', '連線', '工具'],
  },
  { path: '/pdf', title: 'PDF 解鎖', words: ['pdf', '解鎖', '密碼'] },
  {
    path: '/slideshow',
    title: '資料夾幻燈片',
    words: ['幻燈片', '投影片', '照片', 'slideshow', 'photos'],
  },
];

/** Words that only say "take me there", before the page's name (an English one with a space after it). */
const GO_WORDS = ['帶我去', '前往', '打開', '開啟', '進入', '去', 'go to ', 'open '];

const MAX_ACTIONS = 3;
/** Longer than this it is a question for the AI, not a page's name. */
const MAX_QUERY = 24;
/** One English word as the dictionary takes it (Studio's open_page drops anything else too), of three
 *  letters or more: "hi" and "ok" open a chat, they are not looked up. */
const ENGLISH_WORD = /^[a-z][a-z'-]{2,29}$/i;
/** Shorter than this, what was typed must be a page's word exactly ("re" would match half the site). */
const MIN_PREFIX = 3;

/** At most this many of the reader's dashboards, so a page or the word still has room. */
const MAX_DASHBOARDS = 2;
/** A dashboard's name is matched anywhere in it from this long ("業績" in 「每月業績追蹤」). */
const MIN_NAME_PART = 2;
/** A longer name is cut in its label. */
const MAX_LABEL_NAME = 20;

const on = (path: string, page: string) => path === page || path.startsWith(`${page}/`);

/** The page's name in what was typed, without "take me to". */
function queryOf(draft: string): string {
  let q = draft.trim().toLowerCase();
  for (const go of GO_WORDS) {
    if (q.startsWith(go) && q.length > go.length) {
      q = q.slice(go.length).trim();
      break;
    }
  }
  return q;
}

/** How well a page fits what was typed: 2 a word of its exactly, 1 a word starting with it, 0 none. */
function fit(page: QuickPage, q: string): number {
  if (page.words.includes(q)) return 2;
  // A single letter or character would match half the site.
  if (q.length >= MIN_PREFIX && page.words.some((w) => w.startsWith(q))) return 1;
  return 0;
}

/** How well a dashboard's name fits what was typed: 2 the whole name, 1 a part of it, 0 neither. */
function fitName(name: string, q: string): number {
  const n = name.trim().toLowerCase();
  if (n === q) return 2;
  return q.length >= MIN_NAME_PART && n.includes(q) ? 1 : 0;
}

const labelName = (name: string) => {
  const n = name.trim();
  return n.length > MAX_LABEL_NAME ? `${n.slice(0, MAX_LABEL_NAME)}…` : n;
};

/**
 * The actions for what the reader has typed on `currentPath`: the pages and the reader's
 * `dashboards` it names, those it names exactly first (never the one they are on), then the word's
 * dictionary page; at most three.
 */
export function quickActionsFor(
  draft: string,
  currentPath: string,
  dashboards: readonly QuickDashboard[] = [],
): QuickAction[] {
  const q = queryOf(draft);
  if (q.length === 0 || q.length > MAX_QUERY) return [];

  const pages = QUICK_PAGES.map((page) => ({
    score: fit(page, q),
    action: {
      key: `page:${page.path}`,
      label: `前往「${page.title}」`,
      path: page.path,
      kind: 'page',
    } satisfies QuickAction,
  })).filter(({ score, action }) => score > 0 && !on(currentPath, action.path));

  const boards = dashboards
    .map((board) => ({
      score: fitName(board.name, q),
      action: {
        key: `dashboard:${board.id}`,
        label: `打開「${labelName(board.name)}」`,
        path: `/dashboards/${encodeURIComponent(board.id)}`,
        kind: 'dashboard',
      } satisfies QuickAction,
    }))
    .filter(({ score, action }) => score > 0 && currentPath !== action.path)
    .sort((a, b) => b.score - a.score)
    .slice(0, MAX_DASHBOARDS);

  // A stable sort: at the same fit, the site's pages come before the reader's dashboards.
  const named = [...pages, ...boards]
    .sort((a, b) => b.score - a.score)
    .map(({ action }): QuickAction => action);

  const word = draft.trim();
  const lookUp: QuickAction[] =
    ENGLISH_WORD.test(word) && !on(currentPath, `/english/dictionary/${word.toLowerCase()}`)
      ? [
          {
            key: `word:${word.toLowerCase()}`,
            label: `查字典：${word.toLowerCase()}`,
            path: `/english/dictionary/${encodeURIComponent(word.toLowerCase())}`,
            kind: 'word',
          },
        ]
      : [];

  // Pages first: a word that is also a page's name ("review") is more often the page.
  return [...named, ...lookUp].slice(0, MAX_ACTIONS);
}
